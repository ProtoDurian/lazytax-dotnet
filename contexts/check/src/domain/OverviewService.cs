using Ctx.Account.Contract.V1;
using Ctx.Charity.Contract.V1;
using Ctx.Check.Contract.V1;
using Ctx.Habit.Contract.V1;
using Ctx.Plan.Contract.V1;
using LazyTax.Platform.Data;

namespace Ctx.Check.Domain;

public sealed record OverviewItem(
  HabitVersionView Version,
  CharityView? Charity,
  bool HasCharity,
  StreakStatus Status,
  long DebtCents,
  DateTime? TodayDeadlineUtc,
  string Proof);

public sealed record OverviewSummary(string Timezone, IReadOnlyList<OverviewItem> Items, long TotalDebtCents, int UsedSkip, int TotalSkip);

/// <summary>
/// The habit overview (LazyTax HabitOverviewService): each habit with its streak, its week, today's deadline
/// and its debt, plus the month's skips. Everything per habit is read once for all habits.
/// </summary>
public sealed class OverviewService(
  ICheckStore store,
  IHabitV1 habit,
  ICharityV1 charity,
  IPlanV1 plan,
  IAccountV1 account,
  IUnitOfWork uow)
{
  public Task<OverviewSummary> Get(Owner owner, int limit, int skip, DateTime? nowUtc, CancellationToken ct) =>
    uow.Start(async () =>
    {
      var userId = owner.UserId ?? throw new ArgumentException("overview needs a user", nameof(owner));
      var now = nowUtc ?? DateTime.UtcNow;
      var today = await account.Today(userId, now, ct); // 404 when the user has no configuration, as LazyTax
      var versions = await habit.ListCurrent(owner, limit, skip, ct);
      var debts = (await store.OpenDebts(owner, ct)).ToDictionary(d => d.HabitId, d => d.Cents);
      var (monthStart, monthEnd) = CheckWindow.MonthOf(today.Today);
      var usedSkip = await store.CountSkips(userId, monthStart, monthEnd, ct);
      var totalSkip = (await plan.Quota(userId, QuotaKind.Skips, ct)).Limit;

      var habitIds = versions.Select(v => v.HabitId).Distinct().ToList();
      var executions = await store.ExecutionsForHabits(owner, habitIds, ct);
      var checks = (await store.ListForDate(owner, habitIds, today.Today, ct)).ToDictionary(c => c.HabitId);

      // Habits usually share a charity, so each one is read once.
      var charities = new Dictionary<Guid, CharityView?>();
      foreach (var id in versions.Select(v => v.CharityId).OfType<Guid>().Distinct())
        charities[id] = await charity.Get(id, ct);

      var items = versions.Select(v => new OverviewItem(
        v,
        v.CharityId is { } c ? charities.GetValueOrDefault(c) : null,
        v.CharityId is not null,
        Streaks.Status(executions.GetValueOrDefault(v.HabitId) ?? [], today.Timezone, v.DeadlineTime, now),
        debts.GetValueOrDefault(v.HabitId),
        TodayDeadline(v, today.Timezone, today.Today, checks.GetValueOrDefault(v.HabitId)),
        ProofKeys.Manual)).ToList();
      return new OverviewSummary(today.Timezone, items, debts.Values.Sum(), usedSkip, totalSkip);
    }, ct);

  /// <summary>Today's check end: the stored check when it exists (it may have been re-timed), else the window the tick will open.</summary>
  public static DateTime? TodayDeadline(HabitVersionView v, string userTz, DateOnly today, DailyCheck? check)
  {
    if (check is not null) return check.DeadlineUtc;
    var scheduled = v.DaysOfWeek.Any(d => string.Equals(d, today.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase));
    return scheduled ? CheckWindow.For(today, CheckWindow.Resolve(userTz), v.DeadlineTime).DeadlineUtc : null;
  }
}
