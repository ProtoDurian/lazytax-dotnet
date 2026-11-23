namespace Ctx.Habit.Data;

/// <summary>A routine: a named group of habits. Soft deleted; it lives while it has a live habit.</summary>
public sealed class RoutineRow
{
  public Guid Id { get; set; } = Guid.NewGuid();
  public required string UserId { get; set; }
  public required string Name { get; set; }
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
  public DateTime? DeletedAt { get; set; }
}

/// <summary>A habit: a stable id plus a pointer to its current version. Soft deleted.</summary>
public sealed class HabitRow
{
  public Guid Id { get; set; } = Guid.NewGuid();
  public required string UserId { get; set; }
  public int Version { get; set; }
  public bool Enabled { get; set; } = true;
  public int SortOrder { get; set; }
  public Guid? RoutineId { get; set; }
  public DateTime? DeletedAt { get; set; }
  public ICollection<HabitVersionRow> Versions { get; set; } = [];
}

/// <summary>One immutable version of a habit's blueprint. The charity is another context's row: no foreign key.</summary>
public sealed class HabitVersionRow
{
  public Guid Id { get; set; } = Guid.NewGuid();
  public Guid HabitId { get; set; }
  public Guid? CharityId { get; set; }
  public int Version { get; set; }
  public required string Task { get; set; }
  public required string[] DaysOfWeek { get; set; }
  public TimeOnly NotificationTime { get; set; }
  public TimeOnly DeadlineTime { get; set; } = new(23, 59);
  public int StakeCents { get; set; }
  public string StakeCurrency { get; set; } = "USD";
  public int RatioBasisPoints { get; set; }
  public required string Timezone { get; set; }
  public HabitRow? Habit { get; set; }
}
