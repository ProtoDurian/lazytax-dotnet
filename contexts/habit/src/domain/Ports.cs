using Ctx.Habit.Contract.V1;
using LazyTax.Platform.Data;

namespace Ctx.Habit.Domain;

/// <summary>Habit storage. Every read takes an <see cref="Owner"/>; the first read of a request is owner-scoped.</summary>
public interface IHabitRepository
{
  Task<HabitVersionView?> GetCurrent(Owner owner, Guid habitId, CancellationToken ct);
  Task<HabitVersionView?> GetVersion(Owner owner, Guid versionId, CancellationToken ct);
  Task<IReadOnlyList<HabitVersionView>> GetCurrentMany(Owner owner, IReadOnlyCollection<Guid> habitIds, CancellationToken ct);
  Task<IReadOnlyList<HabitVersionView>> Search(Owner owner, HabitSearch search, CancellationToken ct);
  Task<IReadOnlyList<HabitVersionView>> ListVersions(Owner owner, Guid habitId, CancellationToken ct);
  Task<IReadOnlyList<SchedulableRow>> ListSchedulable(IReadOnlyCollection<string>? userIds, IReadOnlyCollection<Guid>? habitIds, CancellationToken ct);
  Task<int> Count(string userId, CancellationToken ct);

  /// <summary>Inserts the habit (version 1) at the bottom of the user's list in <paramref name="routineId"/>.</summary>
  Task<HabitVersionView> Create(string userId, VersionRecord record, Guid routineId, CancellationToken ct);

  /// <summary>New version plus the enabled flag; null when the habit is not the owner's (or deleted).</summary>
  Task<HabitVersionView?> Update(Owner owner, Guid habitId, VersionRecord record, bool enabled, CancellationToken ct);

  /// <summary>Soft delete; false when the habit is not the owner's. Also soft deletes its routine when that was its last live habit.</summary>
  Task<bool> Delete(Owner owner, Guid habitId, CancellationToken ct);

  Task<IReadOnlyList<Guid>> OrderedIds(Owner owner, CancellationToken ct);
  Task SetOrder(string userId, IReadOnlyList<Guid> orderedIds, CancellationToken ct);
}

public interface IRoutineRepository
{
  Task<RoutineView?> Get(Owner owner, Guid id, CancellationToken ct);
  Task<IReadOnlyList<RoutineView>> Search(Owner owner, RoutineSearch search, CancellationToken ct);
  Task<RoutineView> Create(string userId, string name, CancellationToken ct);
  Task<RoutineView?> Update(Owner owner, Guid id, string name, CancellationToken ct);

  /// <summary>Soft deletes the routine and every live habit in it; returns those habit ids, or null when it is not the owner's.</summary>
  Task<IReadOnlyList<Guid>?> Delete(Owner owner, Guid id, CancellationToken ct);
}
