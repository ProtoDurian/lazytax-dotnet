namespace Ctx.Protection.Contract.V1;

/// <summary>Outbox event names emitted by protection.</summary>
public static class ProtectionEvents
{
  /// <summary>A perfect week for one habit earned one freeze day (payload <see cref="ProtectionAwarded"/>).</summary>
  public const string Awarded = "protection.awarded";
}

/// <param name="WeekStart">The Sunday that starts the awarded week.</param>
public sealed record ProtectionAwarded(string UserId, Guid HabitId, DateOnly WeekStart, int Balance, int Cap);
