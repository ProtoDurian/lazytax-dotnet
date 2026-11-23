using Ctx.Check.Domain;
using Ctx.Habit.Contract.V1;
using Ctx.Protection.Contract.V1;
using LazyTax.Platform.Data;

namespace Ctx.Check.Data;

/// <summary>
/// <see cref="IHabitSchedule"/> over habit.v1. habit's <c>ListSchedulable</c> already resolves the user's
/// timezone; the stake comes from the current versions (<c>GetCurrentMany</c>).
/// </summary>
public sealed class HabitScheduleAdapter(IHabitV1 habit) : IHabitSchedule
{
  public async Task<IReadOnlyList<Domain.SchedulableHabit>> ListSchedulable(string? userId, IReadOnlyList<Guid>? habitIds, CancellationToken ct)
  {
    var list = await habit.ListSchedulable(userId is null ? null : [userId], habitIds, ct);
    if (list.Count == 0) return [];
    // The tick reads every user's habits, so it asks as a worker (Owner.Anyone); a user call stays scoped.
    var owner = userId is null ? Owner.Anyone() : Owner.User(userId);
    var versions = (await habit.GetCurrentMany(owner, [.. list.Select(h => h.HabitId)], ct)).ToDictionary(v => v.HabitId);
    return
    [
      .. list.Select(h =>
      {
        var v = versions.GetValueOrDefault(h.HabitId);
        return new Domain.SchedulableHabit(h.HabitId, h.UserId, h.VersionId, h.DaysOfWeek, h.DeadlineTime, h.Timezone,
          v?.StakeCents ?? 0, v?.RatioBasisPoints ?? 0, v?.StakeCurrency ?? "USD", v?.CharityId);
      }),
    ];
  }

  public async Task<HabitVersionRef?> GetVersion(Owner owner, Guid versionId, CancellationToken ct)
  {
    var v = await habit.GetVersion(owner, versionId, ct);
    return v is null || v.Deleted ? null : new HabitVersionRef(v.Id, v.HabitId, v.UserId);
  }

  public async Task<IReadOnlyList<Guid>> CurrentVersionIds(Owner owner, CancellationToken ct) =>
    [.. (await habit.ListCurrent(owner, int.MaxValue, 0, ct)).Select(v => v.Id)];
}

/// <summary><see cref="IFreezes"/> over protection.v1.</summary>
public sealed class FreezesAdapter(IProtectionV1 protection) : IFreezes
{
  public Task ReconcileCap(string userId, int maxStreak, CancellationToken ct) => protection.ReconcileCap(userId, maxStreak, ct);

  public Task<bool> TryConsumeFreeze(string userId, DateOnly date, CancellationToken ct) => protection.TryConsumeFreeze(userId, date, ct);
}
