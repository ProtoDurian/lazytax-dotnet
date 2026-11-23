using System.Collections.Concurrent;
using Ctx.Account.Contract.V1;
using Ctx.Charity.Contract.V1;
using Ctx.Habit.Contract.V1;
using Ctx.Plan.Contract.V1;
using Ctx.Protection.Contract.V1;
using Ctx.Vacation.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;
using LazyTax.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Check.Tests.Integration;

/// <summary>
/// The whole app on real Postgres, with check's real code, and in-memory fakes for the contracts check
/// calls (habit, account, plan, vacation, protection, charity), so these tests pin check's behaviour and
/// not other lanes' state. One per test assembly.
/// </summary>
public sealed class CheckApp : KernelApp
{
  public FakeWorld World { get; } = new();

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    base.ConfigureWebHost(builder);
    builder.ConfigureTestServices(s =>
    {
      s.AddSingleton(this.World);
      s.AddScoped<IHabitV1>(_ => this.World.Habits);
      s.AddScoped<IAccountV1>(_ => this.World.Account);
      s.AddScoped<IPlanV1>(_ => this.World.Plan);
      s.AddScoped<IVacationV1>(_ => this.World.Vacation);
      s.AddScoped<IProtectionV1>(_ => this.World.Protection);
      s.AddScoped<ICharityV1>(_ => this.World.Charity);
    });
  }
}

public sealed class FakeWorld
{
  public FakeHabits Habits { get; } = new();
  public FakeAccount Account { get; } = new();
  public FakePlan Plan { get; } = new();
  public FakeVacation Vacation { get; } = new();
  public FakeProtection Protection { get; } = new();
  public FakeCharity Charity { get; } = new();
}

public sealed class FakeHabit
{
  public required Guid HabitId { get; init; }
  public required string UserId { get; init; }
  public Guid VersionId { get; set; } = Guid.NewGuid();
  public ushort Version { get; set; } = 1;
  public string Task { get; set; } = "Run";
  public string[] Days { get; set; } = [.. Enum.GetValues<DayOfWeek>().Select(d => d.ToString())];
  public TimeOnly Deadline { get; set; } = new(23, 59);
  public string Timezone { get; set; } = "UTC";
  public int StakeCents { get; set; } = 1000;
  public Guid? CharityId { get; set; }
  public bool Enabled { get; set; } = true;
  public bool Deleted { get; set; }
  public int SortOrder { get; set; }

  /// <summary>Seen by the all-users tick (userIds null). Off by default, so one test class's tick never settles another's habits.</summary>
  public bool InTick { get; set; }

  public HabitVersionView View() => new(this.VersionId, this.HabitId, this.UserId, this.Version, this.Task, this.Days,
    new TimeOnly(8, 0), this.Deadline, this.CharityId, this.StakeCents, "USD", 10_000, this.Timezone, this.Enabled,
    this.Deleted, true, this.SortOrder, null);
}

public sealed class FakeHabits : IHabitV1
{
  public ConcurrentDictionary<Guid, FakeHabit> All { get; } = new();

  public FakeHabit Add(string userId, Action<FakeHabit>? edit = null)
  {
    var h = new FakeHabit { HabitId = Guid.NewGuid(), UserId = userId, CharityId = Guid.NewGuid() };
    edit?.Invoke(h);
    this.All[h.HabitId] = h;
    return h;
  }

  private static bool Sees(Owner o, FakeHabit h) => o.IsAnyone || o.UserId == h.UserId;

  public Task<IReadOnlyList<SchedulableHabit>> ListSchedulable(IReadOnlyCollection<string>? userIds = null,
    IReadOnlyCollection<Guid>? habitIds = null, CancellationToken ct = default) =>
    Task.FromResult<IReadOnlyList<SchedulableHabit>>([
      .. this.All.Values.Where(h => h.Enabled && !h.Deleted && (userIds is null ? h.InTick : userIds.Contains(h.UserId))
                                    && (habitIds is null || habitIds.Contains(h.HabitId)))
        .Select(h => new SchedulableHabit(h.HabitId, h.UserId, h.VersionId, h.Days, h.Deadline, h.Timezone)),
    ]);

  public Task<HabitVersionView?> GetVersion(Owner owner, Guid versionId, CancellationToken ct = default) =>
    Task.FromResult(this.All.Values.FirstOrDefault(h => h.VersionId == versionId && Sees(owner, h))?.View());

  public Task<HabitVersionView?> GetCurrent(Owner owner, Guid habitId, CancellationToken ct = default) =>
    Task.FromResult(this.All.TryGetValue(habitId, out var h) && !h.Deleted && Sees(owner, h) ? h.View() : null);

  public Task<IReadOnlyList<HabitVersionView>> GetCurrentMany(Owner owner, IReadOnlyCollection<Guid> habitIds, CancellationToken ct = default) =>
    Task.FromResult<IReadOnlyList<HabitVersionView>>([
      .. this.All.Values.Where(h => habitIds.Contains(h.HabitId) && !h.Deleted && Sees(owner, h)).Select(h => h.View()),
    ]);

  public Task<IReadOnlyList<HabitVersionView>> ListCurrent(Owner owner, int limit, int skip, CancellationToken ct = default) =>
    Task.FromResult<IReadOnlyList<HabitVersionView>>([
      .. this.All.Values.Where(h => !h.Deleted && Sees(owner, h)).OrderBy(h => h.SortOrder).Skip(skip).Take(limit).Select(h => h.View()),
    ]);

  public Task<IReadOnlyList<HabitVersionView>> ListVersions(Owner owner, Guid habitId, CancellationToken ct = default) =>
    this.GetCurrentMany(owner, [habitId], ct);

  public Task<bool> Owns(string userId, Guid habitId, CancellationToken ct = default) =>
    Task.FromResult(this.All.TryGetValue(habitId, out var h) && !h.Deleted && h.UserId == userId);

  public Task<int> CountHabits(string userId, CancellationToken ct = default) =>
    Task.FromResult(this.All.Values.Count(h => h.UserId == userId && !h.Deleted));
}

public sealed class FakeAccount : IAccountV1
{
  public ConcurrentDictionary<string, string> Timezones { get; } = new();

  public Task<AccountProfile?> GetProfile(Owner owner, CancellationToken ct = default) =>
    Task.FromResult<AccountProfile?>(new AccountProfile(owner.UserId!, owner.UserId!, "x@example.test", true, true));

  public Task<bool> Exists(string userId, CancellationToken ct = default) => Task.FromResult(true);

  public Task<string?> GetTimezone(string userId, CancellationToken ct = default) =>
    Task.FromResult<string?>(this.Timezones.GetValueOrDefault(userId, "UTC"));

  public Task<TimeOnly> EndOfDay(string userId, CancellationToken ct = default) => Task.FromResult(new TimeOnly(23, 59));

  public Task<Guid?> DefaultCharity(string userId, CancellationToken ct = default) => Task.FromResult<Guid?>(null);

  public Task<UserToday> Today(string userId, DateTime? nowUtc = null, CancellationToken ct = default)
  {
    var tzId = this.Timezones.GetValueOrDefault(userId, "UTC");
    var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
    var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc ?? DateTime.UtcNow, DateTimeKind.Utc), tz);
    return Task.FromResult(new UserToday(tzId, DateOnly.FromDateTime(local)));
  }
}

public sealed class FakePlan : IPlanV1
{
  public ConcurrentDictionary<string, int> Skips { get; } = new();

  public Task<string> EffectiveTier(string userId, CancellationToken ct = default) => Task.FromResult(Tiers.Free);

  public Task<Quota> Quota(string userId, QuotaKind kind, CancellationToken ct = default) =>
    Task.FromResult(new Quota(Tiers.Free, EntitlementKeys.For(kind), kind == QuotaKind.Skips ? this.Skips.GetValueOrDefault(userId, 3) : 2));

  public Task<int> FreezeCap(string userId, int maxStreak, CancellationToken ct = default) => Task.FromResult(0);

  public Task EnsureTier(string userId, string minTier, string feature, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakeVacation : IVacationV1
{
  public ConcurrentDictionary<(string, DateOnly), bool> Away { get; } = new();

  public Task<bool> IsAway(string userId, DateOnly date, CancellationToken ct = default) => Task.FromResult(this.Away.ContainsKey((userId, date)));

  public Task<IReadOnlyList<VacationPeriod>> ListActive(string userId, DateOnly date, CancellationToken ct = default) =>
    Task.FromResult<IReadOnlyList<VacationPeriod>>([]);
}

public sealed class FakeProtection : IProtectionV1
{
  public ConcurrentDictionary<string, int> Balances { get; } = new();
  public ConcurrentDictionary<(string, DateOnly), bool> Consumed { get; } = new();
  public ConcurrentDictionary<string, int> LastMaxStreak { get; } = new();

  public Task<FreezeBalance> ReconcileCap(string userId, int maxStreak, CancellationToken ct = default)
  {
    this.LastMaxStreak[userId] = maxStreak;
    return Task.FromResult(new FreezeBalance(userId, this.Balances.GetValueOrDefault(userId), 10));
  }

  public Task<bool> TryConsumeFreeze(string userId, DateOnly date, CancellationToken ct = default)
  {
    if (this.Consumed.ContainsKey((userId, date))) return Task.FromResult(true);
    var b = this.Balances.GetValueOrDefault(userId);
    if (b <= 0) return Task.FromResult(false);
    this.Balances[userId] = b - 1;
    this.Consumed[(userId, date)] = true;
    return Task.FromResult(true);
  }

  public Task<FreezeBalance> Balance(Owner owner, CancellationToken ct = default) =>
    owner.IsAnyone ? throw new Forbidden() : Task.FromResult(new FreezeBalance(owner.UserId!, this.Balances.GetValueOrDefault(owner.UserId!), 10));
}

public sealed class FakeCharity : ICharityV1
{
  public Task<CharityView?> Get(Guid id, CancellationToken ct = default) =>
    Task.FromResult<CharityView?>(new CharityView(id, "Charity " + id.ToString()[..4], null, null, [], null, null, null, null));

  public Task<bool> Exists(Guid id, CancellationToken ct = default) => Task.FromResult(true);

  public Task<IReadOnlyList<CharityView>> Search(CharitySearchQuery q, CancellationToken ct = default) =>
    Task.FromResult<IReadOnlyList<CharityView>>([]);

  public Task<CharityView?> GetByExternalId(string source, string externalKey, CancellationToken ct = default) =>
    Task.FromResult<CharityView?>(null);

  public Task<string?> GetExternalKey(Guid charityId, string source, CancellationToken ct = default) =>
    Task.FromResult<string?>(null);
}
