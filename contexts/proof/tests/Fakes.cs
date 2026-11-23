using System.Collections.Concurrent;
using Ctx.Account.Contract.V1;
using Ctx.Check.Contract.V1;
using Ctx.Habit.Contract.V1;
using Ctx.Plan.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;

namespace Ctx.Proof.Tests;

/// <summary>In-memory habit context: which user owns which habit.</summary>
public sealed class FakeHabits : IHabitV1
{
  private readonly ConcurrentDictionary<Guid, string> owners = new();

  public Guid Add(string userId)
  {
    var id = Guid.NewGuid();
    this.owners[id] = userId;
    return id;
  }

  public void Remove(Guid id) => this.owners.TryRemove(id, out _);

  public Task<bool> Owns(string userId, Guid habitId, CancellationToken ct = default) =>
    Task.FromResult(this.owners.TryGetValue(habitId, out var o) && o == userId);

  public Task<HabitVersionView?> GetCurrent(Owner owner, Guid habitId, CancellationToken ct = default) =>
    Task.FromResult(this.owners.TryGetValue(habitId, out var o) && (owner.IsAnyone || owner.UserId == o)
      ? new HabitVersionView(Guid.NewGuid(), habitId, o, 1, "Run", ["Mon"], new TimeOnly(8, 0), new TimeOnly(23, 59, 59), null, 500, "USD", 10000, "UTC", true, false, true, 0, null)
      : null);

  public Task<IReadOnlyList<SchedulableHabit>> ListSchedulable(IReadOnlyCollection<string>? userIds = null, IReadOnlyCollection<Guid>? habitIds = null, CancellationToken ct = default) => throw new NotImplementedException();
  public Task<HabitVersionView?> GetVersion(Owner owner, Guid versionId, CancellationToken ct = default) => throw new NotImplementedException();
  public Task<IReadOnlyList<HabitVersionView>> GetCurrentMany(Owner owner, IReadOnlyCollection<Guid> habitIds, CancellationToken ct = default) => throw new NotImplementedException();
  public Task<IReadOnlyList<HabitVersionView>> ListCurrent(Owner owner, int limit, int skip, CancellationToken ct = default) => throw new NotImplementedException();
  public Task<IReadOnlyList<HabitVersionView>> ListVersions(Owner owner, Guid habitId, CancellationToken ct = default) => throw new NotImplementedException();
  public Task<int> CountHabits(string userId, CancellationToken ct = default) => throw new NotImplementedException();
}

/// <summary>In-memory check context: one pending check per owned habit and day; (habit, proof, external id) is idempotent.</summary>
public sealed class FakeCheck(FakeHabits habits) : ICheckV1
{
  private readonly object gate = new();
  private readonly HashSet<(Guid, string, string)> seen = [];
  private readonly Dictionary<(Guid, DateOnly), HabitExecution> executions = [];

  public List<(string UserId, Guid HabitId, Evidence Evidence)> Reports { get; } = [];

  /// <summary>Habits check refuses with 404 (switched off).</summary>
  public HashSet<Guid> Disabled { get; } = [];

  /// <summary>When set, PendingCheckAt returns null (the day is settled, unscheduled or over).</summary>
  public bool NothingPending { get; set; }

  private async Task Live(string userId, Guid habitId)
  {
    if (!await habits.Owns(userId, habitId) || this.Disabled.Contains(habitId)) throw new NotFound("Habit", habitId.ToString());
  }

  public async Task<HabitExecution?> Report(string userId, Guid habitId, Evidence evidence, CancellationToken ct = default)
  {
    await this.Live(userId, habitId);
    lock (this.gate)
    {
      if (!this.seen.Add((habitId, evidence.ProofKey, evidence.ExternalId))) return null;
      var date = DateOnly.FromDateTime(evidence.OccurredAtUtc);
      var ex = new HabitExecution(Guid.NewGuid(), habitId, Guid.NewGuid(), userId, date, CheckStatus.Done, evidence.OccurredAtUtc, evidence.Notes);
      this.executions[(habitId, date)] = ex;
      this.Reports.Add((userId, habitId, evidence));
      return ex;
    }
  }

  public async Task<DailyCheck?> PendingCheckAt(string userId, Guid habitId, DateTime atUtc, CancellationToken ct = default)
  {
    await this.Live(userId, habitId);
    if (this.NothingPending) return null;
    var d = DateOnly.FromDateTime(atUtc);
    return new DailyCheck(Guid.NewGuid(), habitId, userId, d, "UTC", d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), d.ToDateTime(new TimeOnly(23, 59), DateTimeKind.Utc), CheckStatus.Pending, null, null, null);
  }

  public Task<HabitExecution?> ExecutionForDate(Owner owner, Guid habitId, DateOnly date, CancellationToken ct = default) =>
    Task.FromResult(this.executions.TryGetValue((habitId, date), out var e) && (owner.IsAnyone || owner.UserId == e.UserId) ? e : null);

  public Task<IReadOnlyList<HabitStreak>> Streaks(string userId, IReadOnlyList<Guid> habitIds, CancellationToken ct = default) => throw new NotImplementedException();
  public Task<int> MaxStreak(string userId, CancellationToken ct = default) => throw new NotImplementedException();
  public Task<HabitExecution?> GetExecution(Owner owner, Guid executionId, CancellationToken ct = default) => throw new NotImplementedException();
  public Task<bool> OwnsExecution(string userId, Guid executionId, CancellationToken ct = default) => throw new NotImplementedException();
}

public sealed class FakeAccount : IAccountV1
{
  public Task<UserToday> Today(string userId, DateTime? nowUtc = null, CancellationToken ct = default) =>
    Task.FromResult(new UserToday("UTC", DateOnly.FromDateTime(nowUtc ?? DateTime.UtcNow)));

  public Task<AccountProfile?> GetProfile(Owner owner, CancellationToken ct = default) => throw new NotImplementedException();
  public Task<bool> Exists(string userId, CancellationToken ct = default) => Task.FromResult(true);
  public Task<string?> GetTimezone(string userId, CancellationToken ct = default) => Task.FromResult<string?>("UTC");
  public Task<TimeOnly> EndOfDay(string userId, CancellationToken ct = default) => Task.FromResult(new TimeOnly(23, 59));
  public Task<Guid?> DefaultCharity(string userId, CancellationToken ct = default) => Task.FromResult<Guid?>(null);
}

/// <summary>Users in <see cref="Pro"/> pass the pro tier check; everyone else is free.</summary>
public sealed class FakePlan : IPlanV1
{
  public HashSet<string> Pro { get; } = [];

  public Task<string> EffectiveTier(string userId, CancellationToken ct = default) => Task.FromResult(this.Pro.Contains(userId) ? Tiers.Pro : Tiers.Free);

  public Task EnsureTier(string userId, string minTier, string feature, CancellationToken ct = default) =>
    minTier == Tiers.Pro && !this.Pro.Contains(userId) ? throw new TierInsufficient(Tiers.Free, feature, 0) : Task.CompletedTask;

  public Task<Quota> Quota(string userId, QuotaKind kind, CancellationToken ct = default) => throw new NotImplementedException();
  public Task<int> FreezeCap(string userId, int maxStreak, CancellationToken ct = default) => throw new NotImplementedException();
}
