using Ctx.Check.Contract.V1;
using Ctx.Plan.Contract.V1;

namespace Ctx.Check.Domain;

/// <summary>
/// What the scheduler needs to know about an enabled habit's current version to open, settle and stake its
/// checks. <see cref="Timezone"/> is the user's timezone (account), falling back to the version's.
/// </summary>
public sealed record SchedulableHabit(
  Guid HabitId,
  string UserId,
  Guid VersionId,
  string[] DaysOfWeek,
  TimeOnly DeadlineTime,
  string Timezone,
  long StakeCents,
  int RatioBps,
  string Currency,
  Guid? CharityId)
{
  public bool IsScheduledOn(DateOnly date) =>
    this.DaysOfWeek.Any(d => string.Equals(d, date.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>A habit version as the routes see it (complete and skip take a version id).</summary>
public sealed record HabitVersionRef(Guid VersionId, Guid HabitId, string UserId);

public sealed record NewCheck(Guid HabitId, string UserId, DateOnly LocalDate, string Timezone, DateTime OpensAtUtc, DateTime DeadlineUtc);

/// <summary>The monthly skip limit and the user-local month it counts in.</summary>
public sealed record SkipQuota(Quota Quota, DateOnly MonthStart, DateOnly MonthEnd);

public sealed record SettleCheck
{
  public required Guid CheckId { get; init; }
  public required SchedulableHabit Habit { get; init; }
  public required CheckStatus Status { get; init; }
  public required string SettledBy { get; init; }
  public required DateTime SettledAtUtc { get; init; }
  public DateTime? CompletedAtUtc { get; init; }
  public string? Notes { get; init; }
  public Evidence? Evidence { get; init; }

  /// <summary>A user's skip: the settle counts the month's skips against this limit before writing one.</summary>
  public SkipQuota? SkipQuota { get; init; }
}

/// <summary>
/// Result of a settle: the execution now recorded for the check's day. <see cref="Adopted"/> is true when the
/// day already had an execution, so nothing new was settled.
/// </summary>
public sealed record SettleOutcome(HabitExecution Execution, Guid CheckId, bool Adopted);

/// <summary>A check settled by the tick or a timezone change (Missed, Frozen or Vacation).</summary>
public sealed record SettledCheck(Guid HabitId, DateOnly LocalDate, CheckStatus Status);

/// <summary>One execution as streaks read it.</summary>
public sealed record ExecutionDay(DateOnly Date, CheckStatus Status, DateTime? CompletedAtUtc);

public sealed record ExecutionSearch(Guid? HabitId, DateOnly? Date, int Limit, int Skip);

/// <summary>The user's open debt of one habit: Missed executions with a money stake (cents).</summary>
public sealed record HabitDebt(Guid HabitId, long Cents);
