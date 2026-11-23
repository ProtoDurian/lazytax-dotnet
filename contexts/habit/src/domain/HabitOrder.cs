using LazyTax.Platform.Problems;

namespace Ctx.Habit.Domain;

public static class HabitOrder
{
  public const string Field = "habitIds";

  /// <summary>
  /// The user's new habit order: the requested ids first, in the given order, then the ones not requested in
  /// their current order. Every requested id must be one of the user's live habits (<paramref name="current"/>), at most once.
  /// </summary>
  public static List<Guid> Merge(IReadOnlyList<Guid> current, IReadOnlyList<Guid> requested)
  {
    var currentSet = current.ToHashSet();
    var requestedSet = new HashSet<Guid>();
    foreach (var id in requested)
    {
      if (!requestedSet.Add(id))
        throw new ValidationFailed("Invalid ReorderHabitsReq", Field, $"Habit {id} is listed more than once");
      // Another user's, a deleted and an unknown habit get the same answer.
      if (!currentSet.Contains(id))
        throw new ValidationFailed("Invalid ReorderHabitsReq", Field, $"Habit {id} is not one of your habits");
    }

    return requested.Concat(current.Where(id => !requestedSet.Contains(id))).ToList();
  }
}
