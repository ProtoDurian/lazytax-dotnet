namespace Ctx.Check.Contract.V1;

/// <summary>Outbox event names emitted by check.</summary>
public static class CheckEvents
{
  /// <summary>
  /// One check settled (payload <see cref="CheckSettled"/>), written in the settle's unit of work (LazyTax
  /// CheckEvents "CheckSettled"). penalty stakes it, protection awards freezes from it, friend posts friend
  /// stakes from it.
  /// </summary>
  public const string Settled = "check.settled";
}

/// <summary>The settled statuses as they appear in <see cref="CheckSettled.Status"/> (lowercase strings, as in Bun).</summary>
public static class SettledStatus
{
  public const string Done = "done";
  public const string Missed = "missed";
  public const string Skipped = "skipped";
  public const string Frozen = "frozen";
  public const string Vacation = "vacation";
}

/// <summary>
/// Payload of <see cref="CheckEvents.Settled"/> (camelCase JSON). The stake fields are the habit version's at
/// settle time. <see cref="MaxStreak"/> is the user's max streak across habits after this settle.
/// <see cref="Adopted"/> is true when the check only adopted an execution already recorded for that day.
/// </summary>
public sealed record CheckSettled(
  string UserId,
  Guid HabitId,
  Guid HabitVersionId,
  Guid CheckId,
  Guid ExecutionId,
  DateOnly Date,
  string Status,
  long StakeCents,
  int RatioBps,
  string Currency,
  Guid? CharityId,
  int MaxStreak,
  bool Adopted);
