using System.Text.Json;
using Ctx.Proof.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using Microsoft.Extensions.Logging;

namespace Ctx.Proof.Domain;

public enum StravaConnectOutcome
{
  Connected,
  ScopeMissing,
  AthleteTaken,
}

public static class StravaScopes
{
  /// <summary>read_all so runs set to "Only You" count too (decided 2026-11-11).</summary>
  public const string Required = "activity:read_all";
}

/// <summary>
/// The Strava link. HTTP (Strava) never runs inside a transaction: tokens refresh under a short lease, the
/// activity is fetched outside any unit, and only the routing runs in one.
/// </summary>
public sealed class StravaService(
  IStravaApi api,
  IStravaConnections connections,
  IStravaInbox inbox,
  IBindingRepository bindings,
  ProofRouter router,
  IOutbox outbox,
  IUnitOfWork uow,
  TimeProvider clock,
  ILogger<StravaService> logger)
{
  private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);
  private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);
  private static readonly TimeSpan RefreshLease = TimeSpan.FromSeconds(30);

  private DateTime Now => clock.GetUtcNow().UtcDateTime;

  /// <summary>Swaps the callback's code for tokens and links the user's Strava.</summary>
  public async Task<StravaConnectOutcome> Connect(string userId, string code, string grantedScope, CancellationToken ct)
  {
    var scopes = grantedScope.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
    if (!scopes.Contains(StravaScopes.Required)) return StravaConnectOutcome.ScopeMissing;

    var grant = await api.ExchangeCode(code, ct) ?? throw new InvalidOperationException("Strava rejected the authorization code");
    var existing = await connections.GetByAthlete(grant.AthleteId, ct);
    if (existing is not null && existing.UserId != userId)
    {
      await this.RevokeQuietly(grant.Tokens.RefreshToken, ct); // don't leave LazyTax authorized on an account we refuse to link
      return StravaConnectOutcome.AthleteTaken;
    }

    try
    {
      await connections.Upsert(userId, grant.AthleteId, grant.Tokens, string.Join(',', scopes), ct);
    }
    catch (LazyTax.Platform.Problems.Conflict)
    {
      await this.RevokeQuietly(grant.Tokens.RefreshToken, ct);
      return StravaConnectOutcome.AthleteTaken;
    }

    logger.LogInformation("Strava connected for UserId={UserId}", userId);
    return StravaConnectOutcome.Connected;
  }

  public Task<StravaConnection?> Get(Owner owner, CancellationToken ct) => connections.Get(owner, ct);

  /// <summary>Revokes LazyTax in Strava (best effort), then deletes the connection, the Strava bindings and the evidence. False when not connected.</summary>
  public async Task<bool> Disconnect(Owner owner, CancellationToken ct)
  {
    var c = await connections.Get(owner, ct);
    if (c is null) return false;
    await this.RevokeQuietly(c.Tokens.RefreshToken, ct); // revoking the refresh token revokes its access tokens too
    return await uow.Start(() => this.Purge(c.UserId, ct), ct);
  }

  /// <summary>Deletes the connection, the user's Strava bindings and (through an event) the Strava evidence. Runs in a unit of work.</summary>
  public async Task<bool> Purge(string userId, CancellationToken ct)
  {
    var removed = await connections.Delete(userId, ct);
    var gone = await bindings.DeleteForPlugin(userId, ProofPlugins.Strava, ct);
    if (removed || gone > 0)
      await outbox.Add(ProofEvents.EvidenceRevoked, new EvidenceRevoked(userId, ProofPlugins.Strava, null), ct);
    return removed;
  }

  /// <summary>One webhook event, from the inbox.</summary>
  public async Task Process(StravaWebhookEvent e, CancellationToken ct)
  {
    if (e.ObjectType == "athlete")
    {
      if (!e.Deauthorized) return;
      // The user removed LazyTax in Strava's settings: a disconnect, minus the revoke.
      if (await connections.GetByAthlete(e.OwnerId, ct) is not { } a) return;
      logger.LogInformation("Strava deauthorized for UserId={UserId}", a.UserId);
      await uow.Start(() => this.Purge(a.UserId, ct), ct);
      return;
    }

    if (e.ObjectType != "activity") return;
    if (await connections.GetByAthlete(e.OwnerId, ct) is not { } conn) return;

    if (e.AspectType == "delete")
    {
      // Scoped to the athlete's user (LazyTax matched by activity id across all users).
      await uow.Start(() => outbox.Add(ProofEvents.EvidenceRevoked,
        new EvidenceRevoked(conn.UserId, ProofPlugins.Strava, e.ObjectId.ToString()), ct), ct);
      return;
    }

    // create, or update (the sport type may have changed): fetch and route. Replays are idempotent on the activity id.
    var token = await this.AccessToken(conn, ct);
    if (token is null) return;
    var act = await api.GetActivity(token, e.ObjectId, ct);
    if (act is null) return;

    await uow.Start(() => router.Route(new ProofEvent(
      ProofPlugins.Strava,
      conn.UserId,
      e.ObjectId.ToString(),
      DateTime.SpecifyKind(act.StartDate, DateTimeKind.Utc),
      null,
      JsonSerializer.Serialize(act, PayloadJson)), ct), ct);
  }

  /// <summary>Drains the inbox once. A failed event stays claimed until its lease runs out, then is retried; after MaxAttempts it is dropped.</summary>
  public async Task<int> DrainInbox(int batchSize, TimeSpan lease, int maxAttempts, CancellationToken ct)
  {
    var items = await inbox.Claim(batchSize, lease, this.Now, ct);
    foreach (var item in items)
    {
      try
      {
        await this.Process(item.Event, ct);
        await inbox.Delete(item.Id, ct);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        logger.LogWarning(ex, "Strava event {Id} failed (attempt {Attempts})", item.Id, item.Attempts);
        if (item.Attempts >= maxAttempts) await inbox.Delete(item.Id, ct);
      }
    }

    return items.Count;
  }

  /// <summary>A valid access token, refreshing under a lease when it is about to expire. Null when Strava rejected the refresh token: the connection is purged.</summary>
  private async Task<string?> AccessToken(StravaConnection conn, CancellationToken ct)
  {
    if (conn.Tokens.ExpiresAtUtc - RefreshMargin > this.Now) return conn.Tokens.AccessToken;

    var claimed = await connections.TryClaimRefresh(conn.UserId, RefreshLease, this.Now, ct);
    if (claimed is null)
    {
      // Gone, or another worker is refreshing: use what it saved, else fail so the inbox retries after its lease.
      var latest = await connections.Get(Owner.User(conn.UserId), ct);
      if (latest is null) return null;
      if (latest.Tokens.ExpiresAtUtc - RefreshMargin > this.Now) return latest.Tokens.AccessToken;
      throw new InvalidOperationException("Strava token refresh is in progress; retry");
    }

    // Another worker may have refreshed between our read and our claim.
    if (claimed.Tokens.ExpiresAtUtc - RefreshMargin > this.Now)
    {
      await connections.ReleaseRefresh(conn.UserId, null, ct);
      return claimed.Tokens.AccessToken;
    }

    StravaTokens? fresh;
    try
    {
      fresh = await api.Refresh(claimed.Tokens.RefreshToken, ct); // HTTP, outside any unit
    }
    catch
    {
      await connections.ReleaseRefresh(conn.UserId, null, CancellationToken.None);
      throw;
    }

    if (fresh is null)
    {
      logger.LogWarning("Strava refresh token rejected for UserId={UserId}; disconnecting", conn.UserId);
      await uow.Start(() => this.Purge(conn.UserId, ct), ct);
      return null;
    }

    await connections.ReleaseRefresh(conn.UserId, fresh, ct);
    return fresh.AccessToken;
  }

  private async Task RevokeQuietly(string token, CancellationToken ct)
  {
    try
    {
      await api.Revoke(token, ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogWarning(ex, "Strava revoke failed; continuing");
    }
  }
}
