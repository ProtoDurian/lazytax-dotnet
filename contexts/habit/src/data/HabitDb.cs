using Microsoft.EntityFrameworkCore;

namespace Ctx.Habit.Data;

/// <summary>The habit context's one DbContext, in schema <c>habit</c>.</summary>
public sealed class HabitDb(DbContextOptions<HabitDb> options) : DbContext(options)
{
  public DbSet<RoutineRow> Routines => this.Set<RoutineRow>();
  public DbSet<HabitRow> Habits => this.Set<HabitRow>();
  public DbSet<HabitVersionRow> HabitVersions => this.Set<HabitVersionRow>();

  protected override void OnModelCreating(ModelBuilder b)
  {
    b.HasDefaultSchema("habit");

    b.Entity<RoutineRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.Property(x => x.UserId).HasMaxLength(128);
      e.Property(x => x.Name).HasMaxLength(256);
      e.HasIndex(x => x.UserId);
    });

    b.Entity<HabitRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.Property(x => x.UserId).HasMaxLength(128);
      e.HasIndex(x => new { x.UserId, x.DeletedAt });
      e.HasIndex(x => x.RoutineId);
      e.HasOne<RoutineRow>().WithMany().HasForeignKey(x => x.RoutineId).OnDelete(DeleteBehavior.Restrict);
    });

    b.Entity<HabitVersionRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.Property(x => x.Timezone).HasMaxLength(64);
      e.Property(x => x.StakeCurrency).HasMaxLength(8);
      e.HasIndex(x => new { x.HabitId, x.Version }).IsUnique();
      e.HasOne(x => x.Habit).WithMany(h => h.Versions).HasForeignKey(x => x.HabitId).OnDelete(DeleteBehavior.Cascade);
    });
  }
}
