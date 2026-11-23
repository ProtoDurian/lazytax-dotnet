using LazyTax.Platform.Data;

namespace Ctx.Proof.Domain;

public sealed record NfcTag(string Id, string UserId, Guid HabitId, DateTime ClaimedAtUtc);

/// <summary>Proof bindings, one per habit. user_id is a copy of the habit's owner (the habit lives in another context).</summary>
public interface IBindingRepository
{
  /// <summary>The habit's binding if the owner has one.</summary>
  Task<BoundProof?> Get(Owner owner, Guid habitId, CancellationToken ct);

  Task<IReadOnlyList<BoundProof>> ListForUser(Owner owner, string pluginKey, CancellationToken ct);

  Task<IReadOnlyDictionary<Guid, string>> ListPluginKeys(Owner owner, CancellationToken ct);

  /// <summary>Binds the habit, replacing its existing binding (one per habit).</summary>
  Task<BoundProof> Upsert(string userId, Guid habitId, string pluginKey, int pluginVersion, string settings, CancellationToken ct);

  Task<bool> Delete(Owner owner, Guid habitId, CancellationToken ct);

  Task DeleteForHabits(string userId, IReadOnlyCollection<Guid> habitIds, CancellationToken ct);

  /// <summary>Deletes the user's bindings of one plugin; returns how many.</summary>
  Task<int> DeleteForPlugin(string userId, string pluginKey, CancellationToken ct);
}

public interface INfcTagRepository
{
  /// <summary>The tag if the owner claimed it; another user's tag reads as null.</summary>
  Task<NfcTag?> Get(Owner owner, string tagId, CancellationToken ct);

  /// <summary>
  /// Creates the mapping when the tag is unclaimed or the user's own (re-link); one statement, so two users
  /// racing for a tag cannot both win. Null when another user owns it.
  /// </summary>
  Task<NfcTag?> Claim(string userId, string tagId, Guid habitId, CancellationToken ct);

  Task<bool> Delete(Owner owner, string tagId, CancellationToken ct);

  Task<IReadOnlyList<Guid>> ListTaggedHabits(Owner owner, CancellationToken ct);

  Task DeleteForHabits(string userId, IReadOnlyCollection<Guid> habitIds, CancellationToken ct);
}

public sealed record StravaTokens(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc);

public sealed record StravaConnection(Guid Id, string UserId, long AthleteId, StravaTokens Tokens, string Scope, DateTime ConnectedAtUtc);

public interface IStravaConnections
{
  Task<StravaConnection?> Get(Owner owner, CancellationToken ct);

  /// <summary>Worker use: the connection of a Strava athlete (an event names an athlete, not a user).</summary>
  Task<StravaConnection?> GetByAthlete(long athleteId, CancellationToken ct);

  /// <summary>Links (or relinks) the user's Strava. Throws <c>Conflict</c> when the athlete is linked to another user.</summary>
  Task<StravaConnection> Upsert(string userId, long athleteId, StravaTokens tokens, string scope, CancellationToken ct);

  /// <summary>
  /// Takes the refresh lease in one short statement (HTTP never runs inside a transaction, so the old row lock
  /// is a lease). Returns the connection as it is now, or null when the user has none or another worker holds the lease.
  /// </summary>
  Task<StravaConnection?> TryClaimRefresh(string userId, TimeSpan lease, DateTime nowUtc, CancellationToken ct);

  /// <summary>Saves the new tokens (or keeps the current ones when null) and clears the lease.</summary>
  Task ReleaseRefresh(string userId, StravaTokens? tokens, CancellationToken ct);

  Task<bool> Delete(string userId, CancellationToken ct);
}

/// <summary>A webhook event as Strava sends it: IDs only.</summary>
public sealed record StravaWebhookEvent(string ObjectType, string AspectType, long ObjectId, long OwnerId, DateTime EventTimeUtc, bool Deauthorized);

public sealed record StravaInboxItem(Guid Id, int Attempts, StravaWebhookEvent Event);

public interface IStravaInbox
{
  Task Enqueue(StravaWebhookEvent e, CancellationToken ct);

  /// <summary>Claims up to <paramref name="batchSize"/> events (oldest first); an unfinished claim is retried once its lease runs out.</summary>
  Task<IReadOnlyList<StravaInboxItem>> Claim(int batchSize, TimeSpan lease, DateTime nowUtc, CancellationToken ct);

  Task Delete(Guid id, CancellationToken ct);

  Task DeleteForAthlete(long athleteId, CancellationToken ct);
}

/// <summary>What Strava returns for an authorization code.</summary>
public sealed record StravaGrant(long AthleteId, StravaTokens Tokens);

/// <summary>
/// The only activity fields proof reads. Strava's terms allow showing Strava data only to its owner and caching
/// it for at most 7 days, so this lives in memory and is never stored.
/// </summary>
public sealed class StravaActivity
{
  public string SportType { get; set; } = "";
  public DateTime StartDate { get; set; }
  public bool Manual { get; set; }
}

/// <summary>Strava's HTTP API. Only a fake exists in this build (no network).</summary>
public interface IStravaApi
{
  /// <summary>Null when Strava refuses the code.</summary>
  Task<StravaGrant?> ExchangeCode(string code, CancellationToken ct);

  /// <summary>Null when Strava rejects the refresh token (the user deauthorized LazyTax).</summary>
  Task<StravaTokens?> Refresh(string refreshToken, CancellationToken ct);

  /// <summary>Null when the activity doesn't exist (deleted) or the token can't see it.</summary>
  Task<StravaActivity?> GetActivity(string accessToken, long activityId, CancellationToken ct);

  Task Revoke(string token, CancellationToken ct);
}

