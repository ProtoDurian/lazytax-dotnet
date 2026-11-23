using Ctx.Check.Contract.V1;
using LazyTax.Platform.Data;

namespace Ctx.Check.Domain;

/// <summary>
/// check's view of habit.v1 (adapter in <c>data/</c>). Keeps the domain on its own types, so a change in
/// the habit contract touches one adapter.
/// </summary>
public interface IHabitSchedule
{
  /// <summary>Enabled, live habits' current versions, with the user's timezone resolved. Both null means every one (tick only).</summary>
  Task<IReadOnlyList<SchedulableHabit>> ListSchedulable(string? userId, IReadOnlyList<Guid>? habitIds, CancellationToken ct);

  /// <summary>Any version of a live habit, owner-scoped (the first read of complete and skip). Null when none or not the owner's.</summary>
  Task<HabitVersionRef?> GetVersion(Owner owner, Guid versionId, CancellationToken ct);

  /// <summary>The current version ids of the owner's live habits, enabled or not (LazyTax's executions search joins on them).</summary>
  Task<IReadOnlyList<Guid>> CurrentVersionIds(Owner owner, CancellationToken ct);
}

/// <summary>check's view of protection.v1: the freeze balance at settle time.</summary>
public interface IFreezes
{
  /// <summary>Cuts banked freezes to the cap for this max streak (never spend above the cap).</summary>
  Task ReconcileCap(string userId, int maxStreak, CancellationToken ct);

  /// <summary>Spends one freeze for the user-local date; idempotent per (user, date).</summary>
  Task<bool> TryConsumeFreeze(string userId, DateOnly date, CancellationToken ct);
}

/// <summary>check's own rows: checks, evidence and executions (adapter: <c>data/CheckStore</c>).</summary>
public interface ICheckStore
{
  /// <summary>Inserts the checks that don't exist yet (unique per habit and local date).</summary>
  Task<int> CreatePending(IReadOnlyList<NewCheck> checks, CancellationToken ct);

  Task<IReadOnlyList<DailyCheck>> ListDuePending(DateTime deadlineBeforeUtc, int limit, CancellationToken ct);

  Task<IReadOnlyList<DailyCheck>> ListPending(IReadOnlyList<Guid> habitIds, CancellationToken ct);

  Task<IReadOnlyList<DailyCheck>> ListForDate(Owner owner, IReadOnlyList<Guid> habitIds, DateOnly localDate, CancellationToken ct);

  Task<IReadOnlyList<DailyCheck>> ListCovering(Guid habitId, DateTime atUtc, CancellationToken ct);

  Task<DailyCheck?> Get(Guid habitId, DateOnly localDate, CancellationToken ct);

  Task<bool> UpdatePendingWindow(Guid id, string timezone, DateTime opensAtUtc, DateTime deadlineUtc, CancellationToken ct);

  Task<int> DeletePending(IReadOnlyList<Guid> ids, CancellationToken ct);

  /// <summary>Ids of the given checks whose habit already has an execution on the check's date.</summary>
  Task<HashSet<Guid>> ListRecorded(IReadOnlyList<Guid> checkIds, CancellationToken ct);

  Task<bool> EvidenceExists(Guid habitId, string proofKey, string externalId, CancellationToken ct);

  /// <summary>
  /// Settles a Pending check: records (or adopts) the day's execution, links it and records the evidence.
  /// Runs inside the caller's unit of work, after its user lock. Null when the check is no longer Pending
  /// or the evidence was already recorded. Throws <c>TierInsufficient</c> when the month's skips are used up.
  /// </summary>
  Task<SettleOutcome?> Settle(SettleCheck command, CancellationToken ct);

  /// <summary>Every execution of each habit, oldest first; habits without any are absent.</summary>
  Task<Dictionary<Guid, List<ExecutionDay>>> ExecutionsForHabits(Owner owner, IReadOnlyList<Guid> habitIds, CancellationToken ct);

  /// <summary>The longest run of Done days of any of the user's live habits (Missed resets, others don't).</summary>
  Task<int> MaxStreak(string userId, CancellationToken ct);

  Task<int> CountSkips(string userId, DateOnly monthStart, DateOnly monthEnd, CancellationToken ct);

  Task<IReadOnlyList<HabitExecution>> SearchExecutions(Owner owner, IReadOnlyList<Guid> currentVersionIds, ExecutionSearch search, CancellationToken ct);

  Task<IReadOnlyList<HabitDebt>> OpenDebts(Owner owner, CancellationToken ct);

  Task<HabitExecution?> GetExecution(Owner owner, Guid executionId, CancellationToken ct);

  Task<HabitExecution?> ExecutionForDate(Owner owner, Guid habitId, DateOnly date, CancellationToken ct);

  /// <summary>The habits were deleted: drop their Pending checks and hide their executions (LazyTax filters DeletedAt).</summary>
  Task MarkHabitsDeleted(string userId, IReadOnlyList<Guid> habitIds, CancellationToken ct);

  Task Purge(string userId, CancellationToken ct);
}
