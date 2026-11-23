using Ctx.Proof.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;

namespace Ctx.Proof.Tests.Unit;

public sealed class MemBindings : IBindingRepository
{
  public List<BoundProof> Rows { get; } = [];

  public Task<BoundProof?> Get(Owner owner, Guid habitId, CancellationToken ct) =>
    Task.FromResult(this.Rows.FirstOrDefault(r => r.HabitId == habitId && (owner.IsAnyone || r.UserId == owner.UserId)));

  public Task<IReadOnlyList<BoundProof>> ListForUser(Owner owner, string pluginKey, CancellationToken ct) =>
    Task.FromResult<IReadOnlyList<BoundProof>>([.. this.Rows.Where(r => r.PluginKey == pluginKey && (owner.IsAnyone || r.UserId == owner.UserId))]);

  public Task<IReadOnlyDictionary<Guid, string>> ListPluginKeys(Owner owner, CancellationToken ct) =>
    Task.FromResult<IReadOnlyDictionary<Guid, string>>(this.Rows.Where(r => owner.IsAnyone || r.UserId == owner.UserId).ToDictionary(r => r.HabitId, r => r.PluginKey));

  public Task<BoundProof> Upsert(string userId, Guid habitId, string pluginKey, int pluginVersion, string settings, CancellationToken ct)
  {
    this.Rows.RemoveAll(r => r.HabitId == habitId);
    var row = new BoundProof(Guid.NewGuid(), habitId, userId, pluginKey, pluginVersion, settings);
    this.Rows.Add(row);
    return Task.FromResult(row);
  }

  public Task<bool> Delete(Owner owner, Guid habitId, CancellationToken ct) =>
    Task.FromResult(this.Rows.RemoveAll(r => r.HabitId == habitId && (owner.IsAnyone || r.UserId == owner.UserId)) > 0);

  public Task DeleteForHabits(string userId, IReadOnlyCollection<Guid> habitIds, CancellationToken ct)
  {
    this.Rows.RemoveAll(r => r.UserId == userId && habitIds.Contains(r.HabitId));
    return Task.CompletedTask;
  }

  public Task<int> DeleteForPlugin(string userId, string pluginKey, CancellationToken ct) =>
    Task.FromResult(this.Rows.RemoveAll(r => r.UserId == userId && r.PluginKey == pluginKey));
}

public sealed class MemStrava : IStravaConnections
{
  public Dictionary<string, StravaConnection> Rows { get; } = [];
  public HashSet<string> LeaseHeld { get; } = [];

  public Task<StravaConnection?> Get(Owner owner, CancellationToken ct) =>
    Task.FromResult(this.Rows.Values.FirstOrDefault(c => owner.IsAnyone || c.UserId == owner.UserId));

  public Task<StravaConnection?> GetByAthlete(long athleteId, CancellationToken ct) =>
    Task.FromResult(this.Rows.Values.FirstOrDefault(c => c.AthleteId == athleteId));

  public Task<StravaConnection> Upsert(string userId, long athleteId, StravaTokens tokens, string scope, CancellationToken ct)
  {
    var c = new StravaConnection(Guid.NewGuid(), userId, athleteId, tokens, scope, DateTime.UtcNow);
    this.Rows[userId] = c;
    return Task.FromResult(c);
  }

  public Task<StravaConnection?> TryClaimRefresh(string userId, TimeSpan lease, DateTime nowUtc, CancellationToken ct)
  {
    if (!this.Rows.TryGetValue(userId, out var c) || !this.LeaseHeld.Add(userId)) return Task.FromResult<StravaConnection?>(null);
    return Task.FromResult<StravaConnection?>(c);
  }

  public Task ReleaseRefresh(string userId, StravaTokens? tokens, CancellationToken ct)
  {
    this.LeaseHeld.Remove(userId);
    if (tokens is not null && this.Rows.TryGetValue(userId, out var c)) this.Rows[userId] = c with { Tokens = tokens };
    return Task.CompletedTask;
  }

  public Task<bool> Delete(string userId, CancellationToken ct) => Task.FromResult(this.Rows.Remove(userId));
}

public sealed class MemInbox : IStravaInbox
{
  public Task Enqueue(StravaWebhookEvent e, CancellationToken ct) => Task.CompletedTask;
  public Task<IReadOnlyList<StravaInboxItem>> Claim(int batchSize, TimeSpan lease, DateTime nowUtc, CancellationToken ct) => Task.FromResult<IReadOnlyList<StravaInboxItem>>([]);
  public Task Delete(Guid id, CancellationToken ct) => Task.CompletedTask;
  public Task DeleteForAthlete(long athleteId, CancellationToken ct) => Task.CompletedTask;
}

public sealed class InlineUnit : IUnitOfWork
{
  public int Started { get; private set; }
  public bool Active { get; set; }

  public Task<T> Start<T>(Func<Task<T>> work, CancellationToken ct = default)
  {
    this.Started++;
    return work();
  }

  public Task Start(Func<Task> work, CancellationToken ct = default)
  {
    this.Started++;
    return work();
  }

  public Task LockUser(string userId, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class RecordingOutbox : IOutbox
{
  public List<(string Name, object Payload)> Events { get; } = [];

  public Task Add(string eventName, object payload, CancellationToken ct = default)
  {
    this.Events.Add((eventName, payload));
    return Task.CompletedTask;
  }
}

public sealed class FixedClock(DateTime utc) : TimeProvider
{
  public DateTime Utc { get; set; } = utc;

  public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(this.Utc, DateTimeKind.Utc));
}
