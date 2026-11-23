using LazyTax.Platform.Data;

namespace Ctx.Check.Contract.V1;

/// <summary>
/// check.v1: the daily checks of every habit, their settled executions, streaks and the tick (LazyTax
/// Domain/Check plus the execution, streak and overview parts of Domain/Habit). check owns the complete and
/// skip routes; habit tells check about schedule changes only through <c>habit.changed</c>.
/// Never rename or remove a member once merged; add new ones.
/// </summary>
public interface ICheckV1
{
  /// <summary>
  /// The one way to complete (or fail) a habit: settles the check that was open at
  /// <see cref="Evidence.OccurredAtUtc"/> (clamped to now). Trusted callers only (proof). Null when nothing
  /// changed: a replay of the same (habit, proof key, external id), the day is already settled, or the
  /// evidence came in past deadline + grace. Throws <c>NotFound</c> (404) when the habit is not the user's,
  /// not live or not enabled.
  /// </summary>
  Task<HabitExecution?> Report(string userId, Guid habitId, Evidence evidence, CancellationToken ct = default);

  /// <summary>
  /// The Pending check whose window holds <paramref name="atUtc"/> (clamped to now), opened on demand. Null
  /// when that day is settled, not scheduled, or past grace. Throws <c>NotFound</c> like <see cref="Report"/>.
  /// </summary>
  Task<DailyCheck?> PendingCheckAt(string userId, Guid habitId, DateTime atUtc, CancellationToken ct = default);

  /// <summary>Current and max streak per habit (all versions). Habits with no executions get zeros.</summary>
  Task<IReadOnlyList<HabitStreak>> Streaks(string userId, IReadOnlyList<Guid> habitIds, CancellationToken ct = default);

  /// <summary>The longest streak across all of the user's habits (what plan and protection cap freezes by).</summary>
  Task<int> MaxStreak(string userId, CancellationToken ct = default);

  /// <summary>An execution of the owner's, or null (owner-scoped first read).</summary>
  Task<HabitExecution?> GetExecution(Owner owner, Guid executionId, CancellationToken ct = default);

  /// <summary>True when the execution exists and belongs to the user.</summary>
  Task<bool> OwnsExecution(string userId, Guid executionId, CancellationToken ct = default);

  /// <summary>
  /// The owner's execution of the habit (any version) on that user-local date: a Done one wins, else the
  /// latest. Null when none, or not the owner's.
  /// </summary>
  Task<HabitExecution?> ExecutionForDate(Owner owner, Guid habitId, DateOnly date, CancellationToken ct = default);
}

/// <summary>How a check day ended. <see cref="Pending"/> only appears on checks, never on executions or events.</summary>
public enum CheckStatus
{
  Pending,
  Done,
  Missed,
  Skipped,
  Frozen,
  Vacation,
}

/// <summary>One check per scheduled habit per user-local date (LazyTax HabitChecks). The window is [OpensAtUtc, DeadlineUtc).</summary>
public sealed record DailyCheck(
  Guid Id,
  Guid HabitId,
  string UserId,
  DateOnly LocalDate,
  string Timezone,
  DateTime OpensAtUtc,
  DateTime DeadlineUtc,
  CheckStatus Status,
  DateTime? SettledAtUtc,
  string? SettledBy,
  Guid? ExecutionId);

/// <summary>The settled outcome of one check day (LazyTax HabitExecutions). <see cref="CompletedAtUtc"/> is set only when Done.</summary>
public sealed record HabitExecution(
  Guid Id,
  Guid HabitId,
  Guid HabitVersionId,
  string UserId,
  DateOnly Date,
  CheckStatus Status,
  DateTime? CompletedAtUtc,
  string? Notes);

public enum EvidenceVerdict
{
  Passed,
  Failed,
}

/// <summary>
/// What a proof plugin reports (LazyTax Domain/Check Evidence). (habit, <see cref="ProofKey"/>,
/// <see cref="ExternalId"/>) is the idempotency key: a replay changes nothing.
/// </summary>
public sealed record Evidence(
  string ProofKey,
  string ExternalId,
  EvidenceVerdict Verdict,
  DateTime OccurredAtUtc,
  string? Data = null,
  string? Notes = null);

/// <summary>Current: Done days after the last Missed day, up to today. Max: the longest run of Done days (a Missed day resets it, other statuses don't).</summary>
public sealed record HabitStreak(Guid HabitId, int Current, int Max);

/// <summary>The proof keys check itself knows: the app, widget and plain check-in use <see cref="Manual"/>.</summary>
public static class ProofKeys
{
  public const string Manual = "manual";
}
