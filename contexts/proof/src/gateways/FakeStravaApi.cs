using System.Collections.Concurrent;
using Ctx.Proof.Domain;

namespace Ctx.Proof.Gateways;

/// <summary>
/// Strava with no network. The authorization code <c>athlete-&lt;id&gt;</c> grants that athlete (any other code is
/// refused). Tests and dev seed activities with <see cref="SeedActivity"/> and make a refresh token be rejected
/// with <see cref="RejectRefreshToken"/>. Access tokens are <c>access-&lt;n&gt;</c> and expire after an hour.
/// </summary>
public sealed class FakeStravaApi(TimeProvider clock) : IStravaApi
{
  private readonly ConcurrentDictionary<long, StravaActivity> activities = new();
  private readonly ConcurrentDictionary<string, bool> rejected = new();
  private readonly ConcurrentQueue<string> revoked = new();
  private int counter;

  public IReadOnlyCollection<string> RevokedTokens => [.. this.revoked];

  public int RefreshCalls;
  public int ActivityCalls;

  public void SeedActivity(long id, StravaActivity a) => this.activities[id] = a;

  public void RemoveActivity(long id) => this.activities.TryRemove(id, out _);

  public void RejectRefreshToken(string token) => this.rejected[token] = true;

  public Task<StravaGrant?> ExchangeCode(string code, CancellationToken ct) =>
    Task.FromResult(code.StartsWith("athlete-", StringComparison.Ordinal) && long.TryParse(code["athlete-".Length..], out var id)
      ? new StravaGrant(id, this.NewTokens())
      : null);

  public Task<StravaTokens?> Refresh(string refreshToken, CancellationToken ct)
  {
    Interlocked.Increment(ref this.RefreshCalls);
    return Task.FromResult(this.rejected.ContainsKey(refreshToken) ? null : (StravaTokens?)this.NewTokens());
  }

  public Task<StravaActivity?> GetActivity(string accessToken, long activityId, CancellationToken ct)
  {
    Interlocked.Increment(ref this.ActivityCalls);
    return Task.FromResult(this.activities.TryGetValue(activityId, out var a) ? a : null);
  }

  public Task Revoke(string token, CancellationToken ct)
  {
    this.revoked.Enqueue(token);
    return Task.CompletedTask;
  }

  private StravaTokens NewTokens()
  {
    var n = Interlocked.Increment(ref this.counter);
    return new StravaTokens($"access-{n}", $"refresh-{n}-{Guid.NewGuid():N}", clock.GetUtcNow().UtcDateTime.AddHours(1));
  }
}
