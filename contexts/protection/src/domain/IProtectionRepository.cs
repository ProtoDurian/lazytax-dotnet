using LazyTax.Platform.Data;

namespace Ctx.Protection.Domain;

/// <summary>A user's banked freezes and the last max streak check told us about.</summary>
public sealed record UserProtection(string UserId, int FreezeCurrent, int MaxStreak);

/// <summary>One settled day of a habit, as protection records it from <c>check.settled</c>.</summary>
public sealed record SettledDay(Guid HabitId, DateOnly Date, string Status);

/// <summary>Owner-scoped persistence of the freeze pool. Writes take a trusted <c>userId</c> and run under the user lock.</summary>
public interface IProtectionRepository
{
  Task<UserProtection?> Get(Owner owner, CancellationToken ct);

  /// <summary>Gets the row, creating it with 0 freezes when absent (LazyTax <c>UpsertProtection</c>).</summary>
  Task<UserProtection> Upsert(string userId, CancellationToken ct);

  Task SetMaxStreak(string userId, int maxStreak, CancellationToken ct);

  /// <summary>Cuts the balance to <paramref name="cap"/> when above it. Cut freezes are gone for good.</summary>
  Task Clamp(string userId, int cap, CancellationToken ct);

  Task Add(string userId, int n, CancellationToken ct);

  /// <summary>True when this date was already consumed, or one freeze was just spent; false when the balance is 0.</summary>
  Task<bool> TryConsume(string userId, DateOnly date, CancellationToken ct);

  /// <summary>Records the (habit, week) award; false when it was already recorded (idempotent).</summary>
  Task<bool> RecordAwardIfAbsent(string userId, Guid habitId, DateOnly weekStart, CancellationToken ct);

  Task RecordSettled(string userId, Guid habitId, DateOnly date, string status, CancellationToken ct);

  Task<IReadOnlyList<SettledDay>> SettledBetween(Owner owner, DateOnly from, DateOnly to, CancellationToken ct);

  Task<IReadOnlyList<string>> UsersSettledSince(DateOnly from, CancellationToken ct);

  Task PruneSettledBefore(DateOnly before, CancellationToken ct);
}
