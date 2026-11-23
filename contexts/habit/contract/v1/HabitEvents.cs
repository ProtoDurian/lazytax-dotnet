namespace Ctx.Habit.Contract.V1;

/// <summary>Outbox event names emitted by habit.</summary>
public static class HabitEvents
{
  /// <summary>
  /// A habit was created, updated, reordered or deleted (payload <see cref="HabitChanged"/>). check subscribes to
  /// retime pending checks (LazyTax called <c>checkService.RetimePending</c> synchronously).
  /// </summary>
  public const string Changed = "habit.changed";

  /// <summary>
  /// A habit (or a whole routine's habits) was soft-deleted (payload <see cref="HabitDeleted"/>). proof subscribes
  /// to release the NFC tags (LazyTax deleted <c>NfcTags</c> inside <c>HabitRepository.Delete</c>).
  /// </summary>
  public const string Deleted = "habit.deleted";
}

public sealed record HabitChanged(string UserId, Guid[] HabitIds);

public sealed record HabitDeleted(string UserId, Guid[] HabitIds);
