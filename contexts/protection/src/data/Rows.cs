namespace Ctx.Protection.Data;

public sealed class UserProtectionRow
{
  public Guid Id { get; set; }
  public required string UserId { get; set; }
  public int FreezeCurrent { get; set; }
  public int MaxStreak { get; set; }
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class FreezeConsumptionRow
{
  public Guid Id { get; set; }
  public required string UserId { get; set; }
  public DateOnly Date { get; set; }
  public DateTime ConsumedAt { get; set; } = DateTime.UtcNow;
}

public sealed class FreezeAwardRow
{
  public Guid Id { get; set; }
  /// <summary>A copy of the owner (the habit lives in another context), so the purge and owner filters work.</summary>
  public required string UserId { get; set; }
  public Guid HabitId { get; set; }
  public DateOnly WeekStart { get; set; }
  public DateTime AwardedAt { get; set; } = DateTime.UtcNow;
}

public sealed class SettledDayRow
{
  public Guid Id { get; set; }
  public required string UserId { get; set; }
  public Guid HabitId { get; set; }
  public DateOnly Date { get; set; }
  public required string Status { get; set; }
  public DateTime SettledAt { get; set; } = DateTime.UtcNow;
}
