using Ctx.Proof.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ctx.Proof.Data;

public sealed class BindingRepository(ProofDb db) : IBindingRepository
{
  public async Task<BoundProof?> Get(Owner owner, Guid habitId, CancellationToken ct) =>
    (await db.ProofBindings.AsNoTracking().OwnedBy(owner, x => x.UserId).FirstOrDefaultAsync(x => x.HabitId == habitId, ct))?.ToDomain();

  public async Task<IReadOnlyList<BoundProof>> ListForUser(Owner owner, string pluginKey, CancellationToken ct) =>
    [.. (await db.ProofBindings.AsNoTracking().OwnedBy(owner, x => x.UserId).Where(x => x.PluginKey == pluginKey).ToListAsync(ct)).Select(x => x.ToDomain())];

  public async Task<IReadOnlyDictionary<Guid, string>> ListPluginKeys(Owner owner, CancellationToken ct) =>
    await db.ProofBindings.AsNoTracking().OwnedBy(owner, x => x.UserId).ToDictionaryAsync(x => x.HabitId, x => x.PluginKey, ct);

  public async Task<BoundProof> Upsert(string userId, Guid habitId, string pluginKey, int pluginVersion, string settings, CancellationToken ct)
  {
    var row = await db.ProofBindings.FirstOrDefaultAsync(x => x.HabitId == habitId && x.UserId == userId, ct);
    if (row is null)
    {
      db.ProofBindings.Add(row = new ProofBindingRow
      {
        Id = Guid.NewGuid(),
        UserId = userId,
        HabitId = habitId,
        PluginKey = pluginKey,
        PluginVersion = pluginVersion,
        Settings = settings,
      });
    }
    else
    {
      row.PluginKey = pluginKey;
      row.PluginVersion = pluginVersion;
      row.Settings = settings;
      row.UpdatedAtUtc = DateTime.UtcNow;
    }

    await db.SaveChangesAsync(ct);
    return row.ToDomain();
  }

  public async Task<bool> Delete(Owner owner, Guid habitId, CancellationToken ct) =>
    await db.ProofBindings.OwnedBy(owner, x => x.UserId).Where(x => x.HabitId == habitId).ExecuteDeleteAsync(ct) > 0;

  public Task DeleteForHabits(string userId, IReadOnlyCollection<Guid> habitIds, CancellationToken ct) =>
    db.ProofBindings.Where(x => x.UserId == userId && habitIds.Contains(x.HabitId)).ExecuteDeleteAsync(ct);

  public Task<int> DeleteForPlugin(string userId, string pluginKey, CancellationToken ct) =>
    db.ProofBindings.Where(x => x.UserId == userId && x.PluginKey == pluginKey).ExecuteDeleteAsync(ct);
}

internal static class RowMappers
{
  public static BoundProof ToDomain(this ProofBindingRow r) => new(r.Id, r.HabitId, r.UserId, r.PluginKey, r.PluginVersion, r.Settings);

  public static NfcTag ToDomain(this NfcTagRow r) => new(r.Id, r.UserId, r.HabitId, DateTime.SpecifyKind(r.ClaimedAtUtc, DateTimeKind.Utc));
}

public sealed class NfcTagRepository(ProofDb db) : INfcTagRepository
{
  public async Task<NfcTag?> Get(Owner owner, string tagId, CancellationToken ct) =>
    (await db.NfcTags.AsNoTracking().OwnedBy(owner, x => x.UserId).FirstOrDefaultAsync(x => x.Id == tagId, ct))?.ToDomain();

  public async Task<NfcTag?> Claim(string userId, string tagId, Guid habitId, CancellationToken ct)
  {
    // One statement: insert, or re-point only when the row is already this user's. A foreign tag is never loaded.
    var rows = await db.NfcTags.FromSql($"""
      INSERT INTO proof."NfcTags" ("Id", "UserId", "HabitId", "ClaimedAtUtc")
      VALUES ({tagId}, {userId}, {habitId}, now())
      ON CONFLICT ("Id") DO UPDATE SET "HabitId" = EXCLUDED."HabitId"
      WHERE proof."NfcTags"."UserId" = EXCLUDED."UserId"
      RETURNING *
      """).AsNoTracking().ToListAsync(ct);
    return rows.FirstOrDefault()?.ToDomain();
  }

  public async Task<bool> Delete(Owner owner, string tagId, CancellationToken ct) =>
    await db.NfcTags.OwnedBy(owner, x => x.UserId).Where(x => x.Id == tagId).ExecuteDeleteAsync(ct) > 0;

  public async Task<IReadOnlyList<Guid>> ListTaggedHabits(Owner owner, CancellationToken ct) =>
    await db.NfcTags.AsNoTracking().OwnedBy(owner, x => x.UserId).Select(x => x.HabitId).Distinct().ToListAsync(ct);

  public Task DeleteForHabits(string userId, IReadOnlyCollection<Guid> habitIds, CancellationToken ct) =>
    db.NfcTags.Where(x => x.UserId == userId && habitIds.Contains(x.HabitId)).ExecuteDeleteAsync(ct);
}

public sealed class StravaConnectionRepository(ProofDb db, TokenBox box) : IStravaConnections
{
  public async Task<StravaConnection?> Get(Owner owner, CancellationToken ct) =>
    this.ToDomain(await db.StravaConnections.AsNoTracking().OwnedBy(owner, x => x.UserId).FirstOrDefaultAsync(ct));

  public async Task<StravaConnection?> GetByAthlete(long athleteId, CancellationToken ct) =>
    this.ToDomain(await db.StravaConnections.AsNoTracking().FirstOrDefaultAsync(x => x.AthleteId == athleteId, ct));

  public async Task<StravaConnection> Upsert(string userId, long athleteId, StravaTokens tokens, string scope, CancellationToken ct)
  {
    try
    {
      var row = await db.StravaConnections.FirstOrDefaultAsync(x => x.UserId == userId, ct);
      if (row is null)
      {
        db.StravaConnections.Add(row = new StravaConnectionRow
        {
          Id = Guid.NewGuid(),
          UserId = userId,
          AthleteId = athleteId,
          AccessToken = box.Encrypt(tokens.AccessToken),
          RefreshToken = box.Encrypt(tokens.RefreshToken),
          ExpiresAtUtc = tokens.ExpiresAtUtc,
          Scope = scope,
        });
      }
      else
      {
        row.AthleteId = athleteId;
        row.AccessToken = box.Encrypt(tokens.AccessToken);
        row.RefreshToken = box.Encrypt(tokens.RefreshToken);
        row.ExpiresAtUtc = tokens.ExpiresAtUtc;
        row.Scope = scope;
        row.RefreshLeaseUntilUtc = null;
        row.ConnectedAtUtc = DateTime.UtcNow;
        row.UpdatedAtUtc = DateTime.UtcNow;
      }

      await db.SaveChangesAsync(ct);
      return this.ToDomain(row)!;
    }
    catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
    {
      db.ChangeTracker.Clear();
      throw new Conflict("Strava account is linked to another user");
    }
  }

  public async Task<StravaConnection?> TryClaimRefresh(string userId, TimeSpan lease, DateTime nowUtc, CancellationToken ct)
  {
    var until = nowUtc + lease;
    var rows = await db.StravaConnections.FromSql($"""
      UPDATE proof."StravaConnections" SET "RefreshLeaseUntilUtc" = {until}
      WHERE "UserId" = {userId} AND ("RefreshLeaseUntilUtc" IS NULL OR "RefreshLeaseUntilUtc" < {nowUtc})
      RETURNING *
      """).AsNoTracking().ToListAsync(ct);
    return this.ToDomain(rows.FirstOrDefault());
  }

  public async Task ReleaseRefresh(string userId, StravaTokens? tokens, CancellationToken ct)
  {
    if (tokens is null)
    {
      await db.StravaConnections.Where(x => x.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RefreshLeaseUntilUtc, (DateTime?)null), ct);
      return;
    }

    var access = box.Encrypt(tokens.AccessToken);
    var refresh = box.Encrypt(tokens.RefreshToken);
    await db.StravaConnections.Where(x => x.UserId == userId).ExecuteUpdateAsync(s => s
      .SetProperty(x => x.AccessToken, access)
      .SetProperty(x => x.RefreshToken, refresh)
      .SetProperty(x => x.ExpiresAtUtc, tokens.ExpiresAtUtc)
      .SetProperty(x => x.RefreshLeaseUntilUtc, (DateTime?)null)
      .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), ct);
  }

  public async Task<bool> Delete(string userId, CancellationToken ct) =>
    await db.StravaConnections.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct) > 0;

  private StravaConnection? ToDomain(StravaConnectionRow? r) => r is null
    ? null
    : new StravaConnection(
      r.Id, r.UserId, r.AthleteId,
      new StravaTokens(box.Decrypt(r.AccessToken), box.Decrypt(r.RefreshToken), DateTime.SpecifyKind(r.ExpiresAtUtc, DateTimeKind.Utc)),
      r.Scope, DateTime.SpecifyKind(r.ConnectedAtUtc, DateTimeKind.Utc));
}

public sealed class StravaInboxRepository(ProofDb db) : IStravaInbox
{
  public async Task Enqueue(StravaWebhookEvent e, CancellationToken ct)
  {
    db.StravaEvents.Add(new StravaEventRow
    {
      Id = Guid.NewGuid(),
      ObjectType = e.ObjectType,
      AspectType = e.AspectType,
      ObjectId = e.ObjectId,
      OwnerId = e.OwnerId,
      EventTimeUtc = e.EventTimeUtc,
      Deauthorized = e.Deauthorized,
    });
    await db.SaveChangesAsync(ct);
  }

  public async Task<IReadOnlyList<StravaInboxItem>> Claim(int batchSize, TimeSpan lease, DateTime nowUtc, CancellationToken ct)
  {
    var until = nowUtc + lease;
    // Claim and return in one statement; SKIP LOCKED hands each row to one worker across replicas.
    var rows = await db.StravaEvents.FromSql($"""
      UPDATE proof."StravaEvents" SET "Attempts" = "Attempts" + 1, "ClaimedUntilUtc" = {until}
      WHERE "Id" IN (
        SELECT "Id" FROM proof."StravaEvents"
        WHERE "ClaimedUntilUtc" IS NULL OR "ClaimedUntilUtc" < {nowUtc}
        ORDER BY "ReceivedAtUtc" LIMIT {batchSize} FOR UPDATE SKIP LOCKED)
      RETURNING *
      """).AsNoTracking().ToListAsync(ct);
    return [.. rows.OrderBy(x => x.ReceivedAtUtc).Select(x => new StravaInboxItem(x.Id, x.Attempts,
      new StravaWebhookEvent(x.ObjectType, x.AspectType, x.ObjectId, x.OwnerId, DateTime.SpecifyKind(x.EventTimeUtc, DateTimeKind.Utc), x.Deauthorized)))];
  }

  public Task Delete(Guid id, CancellationToken ct) => db.StravaEvents.Where(x => x.Id == id).ExecuteDeleteAsync(ct);

  public Task DeleteForAthlete(long athleteId, CancellationToken ct) => db.StravaEvents.Where(x => x.OwnerId == athleteId).ExecuteDeleteAsync(ct);
}
