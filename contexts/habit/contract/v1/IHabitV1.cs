using LazyTax.Platform.Data;

namespace Ctx.Habit.Contract.V1;

/// <summary>
/// habit.v1: habits, their versions and routines (LazyTax Habit, HabitVersion, Routine). check reads habits
/// only through here; it never touches the tables. Every method that takes an <see cref="Owner"/> is
/// owner-scoped (another user's row reads as null). A <c>string userId</c> means a trusted caller decided it.
/// </summary>
public interface IHabitV1
{
  /// <summary>
  /// Enabled, non-deleted habits with their CURRENT version, for the check tick. <paramref name="userIds"/> and
  /// <paramref name="habitIds"/> narrow the result (null = no narrowing on that field). The timezone is the
  /// user's account timezone, falling back to the habit's own (LazyTax <c>ListSchedulableHabits</c>).
  /// </summary>
  Task<IReadOnlyList<SchedulableHabit>> ListSchedulable(
    IReadOnlyCollection<string>? userIds = null,
    IReadOnlyCollection<Guid>? habitIds = null,
    CancellationToken ct = default);

  /// <summary>
  /// One habit version by id, owner-scoped through its habit (version -> habit -> user). Null when it does not
  /// exist or belongs to another user. Deleted habits are returned (<see cref="HabitVersionView.Deleted"/>) so
  /// history stays readable. <see cref="Owner.Anyone"/> is for workers and admin only.
  /// </summary>
  Task<HabitVersionView?> GetVersion(Owner owner, Guid versionId, CancellationToken ct = default);

  /// <summary>The habit's current version, or null when it is missing, deleted or another user's.</summary>
  Task<HabitVersionView?> GetCurrent(Owner owner, Guid habitId, CancellationToken ct = default);

  /// <summary>The current version of each listed non-deleted habit of the owner (missing ids are skipped).</summary>
  Task<IReadOnlyList<HabitVersionView>> GetCurrentMany(Owner owner, IReadOnlyCollection<Guid> habitIds, CancellationToken ct = default);

  /// <summary>The owner's non-deleted habits (current versions) in their order, paged. Used by the overview.</summary>
  Task<IReadOnlyList<HabitVersionView>> ListCurrent(Owner owner, int limit, int skip, CancellationToken ct = default);

  /// <summary>Every version of the habit, newest first; empty when the habit is not the owner's or is deleted.</summary>
  Task<IReadOnlyList<HabitVersionView>> ListVersions(Owner owner, Guid habitId, CancellationToken ct = default);

  /// <summary>True when the habit exists, is not deleted and belongs to <paramref name="userId"/>.</summary>
  Task<bool> Owns(string userId, Guid habitId, CancellationToken ct = default);

  /// <summary>How many non-deleted habits the user has (disabled ones count, as in LazyTax).</summary>
  Task<int> CountHabits(string userId, CancellationToken ct = default);
}

/// <summary>A habit as the check tick needs it: current version id, days, deadline and the timezone to settle in.</summary>
public sealed record SchedulableHabit(
  Guid HabitId,
  string UserId,
  Guid VersionId,
  string[] DaysOfWeek,
  TimeOnly DeadlineTime,
  string Timezone);

/// <summary>One habit version joined with its habit row.</summary>
public sealed record HabitVersionView(
  Guid Id,
  Guid HabitId,
  string UserId,
  ushort Version,
  string Task,
  string[] DaysOfWeek,
  TimeOnly NotificationTime,
  TimeOnly DeadlineTime,
  Guid? CharityId,
  int StakeCents,
  string StakeCurrency,
  int RatioBasisPoints,
  string Timezone,
  bool Enabled,
  bool Deleted,
  bool IsCurrent,
  int SortOrder,
  Guid? RoutineId);
