using Ctx.Penalty.Contract.V1;
using Ctx.Penalty.Domain;
using LazyTax.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Penalty.Data;

/// <summary>LazyTax PenaltyRepository (+ the Penalties part of DisbursementRepository), on penalty's own schema.</summary>
internal sealed class PenaltyStore(PenaltyDb db) : IPenaltyStore
{
  public async Task<bool> Enqueue(NewPenalty p, DateTime now, CancellationToken ct)
  {
    // The unique (HabitExecutionId) index makes this atomic: a replayed or racing event is a no-op.
    var n = await db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO penalty."Penalties"
        ("HabitExecutionId", "HabitId", "UserId", "CharityId", "AmountCents", "Currency", "Status", "Attempts", "CreatedAt", "UpdatedAt")
      VALUES ({p.HabitExecutionId}, {p.HabitId}, {p.UserId}, {p.CharityId}, {p.AmountCents}, {p.Currency}, {PenaltyStatus.Pending}, 0, {now}, {now})
      ON CONFLICT ("HabitExecutionId") DO NOTHING
      """, ct);
    return n > 0;
  }

  public async Task<IReadOnlyList<PenaltyInfo>> ClaimPending(int batchSize, DateTime staleBefore, DateTime now, CancellationToken ct)
  {
    // Atomically CLAIM a batch: pending (or stale processing) -> processing, returned in one statement.
    // FOR UPDATE SKIP LOCKED hands each row to exactly one drain, so overlapping drains never double-charge.
    var rows = await db.Penalties.FromSqlInterpolated($"""
      UPDATE penalty."Penalties" SET "Status" = {PenaltyStatus.Processing}, "UpdatedAt" = {now}
      WHERE "Id" IN (
        SELECT "Id" FROM penalty."Penalties"
        WHERE "Status" = {PenaltyStatus.Pending}
           OR ("Status" = {PenaltyStatus.Processing} AND "UpdatedAt" < {staleBefore})
        ORDER BY "CreatedAt"
        LIMIT {batchSize}
        FOR UPDATE SKIP LOCKED)
      RETURNING *
      """).AsNoTracking().ToListAsync(ct);
    return [.. rows.OrderBy(r => r.CreatedAt).Select(ToInfo)];
  }

  public async Task<PenaltyInfo?> MarkCharged(Guid id, string paymentIntentId, DateTime now, CancellationToken ct)
  {
    // Lock the row so a drain and a late payment.completed for the same penalty serialize here; the second
    // then sees charged and no-ops (the credit below is not idempotent). ToListAsync keeps FOR UPDATE verbatim.
    var row = (await db.Penalties
        .FromSqlInterpolated($"""SELECT * FROM penalty."Penalties" WHERE "Id" = {id} FOR UPDATE""")
        .ToListAsync(ct))
      .FirstOrDefault();
    if (row is null || row.Status == PenaltyStatus.Charged) return null;

    row.Status = PenaltyStatus.Charged;
    row.PaymentIntentId = paymentIntentId;
    row.LastError = null;
    row.UpdatedAt = now;
    await db.SaveChangesAsync(ct);

    // Credit the (charity, currency) ledger in place. An upsert, so two first credits never collide.
    await db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO penalty."CharityBalances" ("CharityId", "Currency", "AccruedCents", "UpdatedAt")
      VALUES ({row.CharityId}, {row.Currency}, {row.AmountCents}, {now})
      ON CONFLICT ("CharityId", "Currency") DO UPDATE
        SET "AccruedCents" = penalty."CharityBalances"."AccruedCents" + EXCLUDED."AccruedCents",
            "UpdatedAt" = EXCLUDED."UpdatedAt"
      """, ct);
    return ToInfo(row);
  }

  private IQueryable<PenaltyRow> Claimed(Guid id) =>
    db.Penalties.Where(x => x.Id == id && x.Status == PenaltyStatus.Processing);

  public async Task<bool> MarkPending(Guid id, string? paymentIntentId, int attempts, DateTime now, CancellationToken ct) =>
    await this.Claimed(id).ExecuteUpdateAsync(s => s
      .SetProperty(x => x.Status, PenaltyStatus.Pending)
      .SetProperty(x => x.PaymentIntentId, x => paymentIntentId ?? x.PaymentIntentId)
      .SetProperty(x => x.Attempts, attempts)
      .SetProperty(x => x.UpdatedAt, now), ct) > 0;

  public async Task<bool> Bump(Guid id, string error, DateTime now, CancellationToken ct) =>
    await this.Claimed(id).ExecuteUpdateAsync(s => s
      .SetProperty(x => x.Status, PenaltyStatus.Pending)
      .SetProperty(x => x.Attempts, x => x.Attempts + 1)
      .SetProperty(x => x.LastError, Trim(error))
      .SetProperty(x => x.UpdatedAt, now), ct) > 0;

  public async Task<bool> MarkFailed(Guid id, string error, DateTime now, CancellationToken ct) =>
    await this.Claimed(id).ExecuteUpdateAsync(s => s
      .SetProperty(x => x.Status, PenaltyStatus.Failed)
      .SetProperty(x => x.Attempts, x => x.Attempts + 1)
      .SetProperty(x => x.LastError, Trim(error))
      .SetProperty(x => x.UpdatedAt, now), ct) > 0;

  public async Task<bool> MarkSkipped(Guid id, DateTime now, CancellationToken ct) =>
    await this.Claimed(id).ExecuteUpdateAsync(s => s
      .SetProperty(x => x.Status, PenaltyStatus.Skipped)
      .SetProperty(x => x.UpdatedAt, now), ct) > 0;

  public async Task<PenaltyInfo?> Get(Owner owner, Guid id, CancellationToken ct)
  {
    var row = await db.Penalties.AsNoTracking().OwnedBy(owner, x => x.UserId).FirstOrDefaultAsync(x => x.Id == id, ct);
    return row is null ? null : ToInfo(row);
  }

  public async Task<IReadOnlyList<PenaltyInfo>> Search(Owner owner, PenaltySearch search, CancellationToken ct)
  {
    var q = db.Penalties.AsNoTracking().OwnedBy(owner, x => x.UserId);
    if (search.Status is { } s) q = q.Where(x => x.Status == s);
    var rows = await q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip(search.Skip).Take(search.Limit).ToListAsync(ct);
    return [.. rows.Select(ToInfo)];
  }

  public async Task<IReadOnlyList<(string Currency, long TotalCents, int Count)>> Unpaid(string userId, CancellationToken ct)
  {
    var rows = await db.Penalties.AsNoTracking()
      .OwnedBy(Owner.User(userId), x => x.UserId)
      .Where(x => x.Status != PenaltyStatus.Charged)
      .GroupBy(x => x.Currency)
      .Select(g => new { Currency = g.Key, Total = g.Sum(x => x.AmountCents), Count = g.Count(), First = g.Min(x => x.CreatedAt) })
      .ToListAsync(ct);
    // "The first currency owed" = the currency of the oldest unpaid penalty.
    return [.. rows.OrderBy(r => r.First).ThenBy(r => r.Currency, StringComparer.Ordinal).Select(r => (r.Currency, r.Total, r.Count))];
  }

  public async Task<IReadOnlyList<CharityBalance>> CharityBalances(CancellationToken ct)
  {
    var rows = await db.CharityBalances.AsNoTracking().OrderBy(x => x.CharityId).ThenBy(x => x.Currency).ToListAsync(ct);
    return [.. rows.Select(r => new CharityBalance(r.CharityId, r.Currency, r.AccruedCents, r.UpdatedAt))];
  }

  public async Task<IReadOnlyList<PayoutGroup>> PayoutCandidates(long minPayoutCents, CancellationToken ct)
  {
    // Lock the charged-but-unpaid rows until the caller's unit of work ends (LazyTax ClaimPendingPayouts).
    var rows = await db.Penalties.FromSqlInterpolated($"""
      SELECT * FROM penalty."Penalties"
      WHERE "Status" = {PenaltyStatus.Charged} AND "DisbursementId" IS NULL
      ORDER BY "CreatedAt"
      FOR UPDATE SKIP LOCKED
      """).AsNoTracking().ToListAsync(ct);
    return
    [
      .. rows.GroupBy(r => (r.CharityId, r.Currency))
        .Select(g => new PayoutGroup(g.Key.CharityId, g.Key.Currency, g.Sum(x => x.AmountCents)))
        .Where(g => g.AmountCents >= minPayoutCents)
        .OrderByDescending(g => g.AmountCents)
        .ThenBy(g => g.CharityId)
        .ThenBy(g => g.Currency, StringComparer.Ordinal),
    ];
  }

  public async Task<long> ClaimForPayout(Guid disbursementId, Guid charityId, string currency, DateTime now, CancellationToken ct)
  {
    var sums = await db.Database.SqlQuery<long>($"""
      WITH stamped AS (
        UPDATE penalty."Penalties" SET "DisbursementId" = {disbursementId}, "UpdatedAt" = {now}
        WHERE "Status" = {PenaltyStatus.Charged} AND "DisbursementId" IS NULL
          AND "CharityId" = {charityId} AND "Currency" = {currency}
        RETURNING "AmountCents")
      SELECT COALESCE(SUM("AmountCents"), 0)::bigint AS "Value" FROM stamped
      """).ToListAsync(ct);
    return sums.FirstOrDefault();
  }

  public Task ReleasePayout(Guid disbursementId, DateTime now, CancellationToken ct) =>
    db.Penalties.Where(x => x.DisbursementId == disbursementId).ExecuteUpdateAsync(s => s
      .SetProperty(x => x.DisbursementId, (Guid?)null)
      .SetProperty(x => x.UpdatedAt, now), ct);

  public Task<int> Anonymise(string userId, CancellationToken ct) =>
    db.Penalties.Where(x => x.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(x => x.UserId, Stake.DeletedUser), ct);

  private static string Trim(string s) => s.Length <= 2048 ? s : s[..2048];

  private static PenaltyInfo ToInfo(PenaltyRow r) => new(
    r.Id, r.HabitExecutionId, r.HabitId, r.UserId, r.CharityId, r.AmountCents, r.Currency, r.Status,
    r.PaymentIntentId, r.Attempts, r.LastError, r.DisbursementId, r.CreatedAt, r.UpdatedAt);
}
