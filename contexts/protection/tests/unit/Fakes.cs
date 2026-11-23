using Ctx.Account.Contract.V1;
using Ctx.Plan.Contract.V1;
using Ctx.Protection.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using LazyTax.Platform.Problems;

namespace Ctx.Protection.Tests.Unit;

internal sealed class FakeRepo : IProtectionRepository
{
  public readonly Dictionary<string, (int Current, int Max)> Users = [];
  public readonly HashSet<(string, DateOnly)> Consumed = [];
  public readonly HashSet<(Guid, DateOnly)> Awards = [];
  public readonly Dictionary<(Guid, DateOnly), (string User, string Status)> Settled = [];

  public Task<UserProtection?> Get(Owner owner, CancellationToken ct) =>
    Task.FromResult<UserProtection?>(owner.UserId is { } u && this.Users.TryGetValue(u, out var v) ? new(u, v.Current, v.Max) : null);

  public Task<UserProtection> Upsert(string userId, CancellationToken ct)
  {
    if (!this.Users.TryGetValue(userId, out var v)) this.Users[userId] = v = (0, 0);
    return Task.FromResult(new UserProtection(userId, v.Current, v.Max));
  }

  public Task SetMaxStreak(string userId, int maxStreak, CancellationToken ct)
  {
    this.Users[userId] = (this.Users[userId].Current, maxStreak);
    return Task.CompletedTask;
  }

  public Task Clamp(string userId, int cap, CancellationToken ct)
  {
    var v = this.Users[userId];
    this.Users[userId] = (Math.Min(v.Current, Math.Max(0, cap)), v.Max);
    return Task.CompletedTask;
  }

  public Task Add(string userId, int n, CancellationToken ct)
  {
    var v = this.Users[userId];
    this.Users[userId] = (v.Current + n, v.Max);
    return Task.CompletedTask;
  }

  public Task<bool> TryConsume(string userId, DateOnly date, CancellationToken ct)
  {
    if (this.Consumed.Contains((userId, date))) return Task.FromResult(true);
    this.Upsert(userId, ct);
    var v = this.Users[userId];
    if (v.Current <= 0) return Task.FromResult(false);
    this.Users[userId] = (v.Current - 1, v.Max);
    this.Consumed.Add((userId, date));
    return Task.FromResult(true);
  }

  public Task<bool> RecordAwardIfAbsent(string userId, Guid habitId, DateOnly weekStart, CancellationToken ct) =>
    Task.FromResult(this.Awards.Add((habitId, weekStart)));

  public Task RecordSettled(string userId, Guid habitId, DateOnly date, string status, CancellationToken ct)
  {
    this.Settled[(habitId, date)] = (userId, status);
    return Task.CompletedTask;
  }

  public Task<IReadOnlyList<SettledDay>> SettledBetween(Owner owner, DateOnly from, DateOnly to, CancellationToken ct) =>
    Task.FromResult<IReadOnlyList<SettledDay>>([.. this.Settled
      .Where(kv => (owner.IsAnyone || kv.Value.User == owner.UserId) && kv.Key.Item2 >= from && kv.Key.Item2 <= to)
      .Select(kv => new SettledDay(kv.Key.Item1, kv.Key.Item2, kv.Value.Status))]);

  public Task<IReadOnlyList<string>> UsersSettledSince(DateOnly from, CancellationToken ct) =>
    Task.FromResult<IReadOnlyList<string>>([.. this.Settled.Where(kv => kv.Key.Item2 >= from).Select(kv => kv.Value.User).Distinct().Order()]);

  public Task PruneSettledBefore(DateOnly before, CancellationToken ct)
  {
    foreach (var k in this.Settled.Keys.Where(k => k.Item2 < before).ToList()) this.Settled.Remove(k);
    return Task.CompletedTask;
  }
}

internal sealed class FakePlan(int baseCap) : IPlanV1
{
  public int BaseCap { get; set; } = baseCap;
  public Task<string> EffectiveTier(string userId, CancellationToken ct = default) => Task.FromResult(Tiers.Pro);
  public Task<Quota> Quota(string userId, QuotaKind kind, CancellationToken ct = default) => throw new NotSupportedException();
  public Task<int> FreezeCap(string userId, int maxStreak, CancellationToken ct = default) => Task.FromResult(this.BaseCap + (maxStreak >= 30 ? 1 : 0));
  public Task EnsureTier(string userId, string minTier, string feature, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class FakeAccount : IAccountV1
{
  public DateOnly? Today_ { get; set; } = new DateOnly(2026, 11, 23); // a Monday
  public Task<AccountProfile?> GetProfile(Owner owner, CancellationToken ct = default) => throw new NotSupportedException();
  public Task<bool> Exists(string userId, CancellationToken ct = default) => Task.FromResult(true);
  public Task<string?> GetTimezone(string userId, CancellationToken ct = default) => Task.FromResult<string?>("UTC");
  public Task<TimeOnly> EndOfDay(string userId, CancellationToken ct = default) => Task.FromResult(new TimeOnly(23, 59));
  public Task<Guid?> DefaultCharity(string userId, CancellationToken ct = default) => Task.FromResult<Guid?>(null);

  public Task<UserToday> Today(string userId, DateTime? nowUtc = null, CancellationToken ct = default) =>
    this.Today_ is { } d ? Task.FromResult(new UserToday("UTC", d)) : throw new NotFound("Configuration", userId);
}

internal sealed class FakeUow : IUnitOfWork
{
  public int Locks;
  public Task<T> Start<T>(Func<Task<T>> work, CancellationToken ct = default) => work();
  public Task Start(Func<Task> work, CancellationToken ct = default) => work();
  public Task LockUser(string userId, CancellationToken ct = default)
  {
    this.Locks++;
    return Task.CompletedTask;
  }
}

internal sealed class FakeOutbox : IOutbox
{
  public readonly List<(string Name, object Payload)> Added = [];
  public Task Add(string eventName, object payload, CancellationToken ct = default)
  {
    this.Added.Add((eventName, payload));
    return Task.CompletedTask;
  }
}
