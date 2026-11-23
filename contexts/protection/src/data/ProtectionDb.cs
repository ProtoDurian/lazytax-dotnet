using Microsoft.EntityFrameworkCore;

namespace Ctx.Protection.Data;

public sealed class ProtectionDb(DbContextOptions<ProtectionDb> options) : DbContext(options)
{
  public DbSet<UserProtectionRow> UserProtections => this.Set<UserProtectionRow>();
  public DbSet<FreezeConsumptionRow> FreezeConsumptions => this.Set<FreezeConsumptionRow>();
  public DbSet<FreezeAwardRow> FreezeAwards => this.Set<FreezeAwardRow>();
  public DbSet<SettledDayRow> SettledDays => this.Set<SettledDayRow>();

  protected override void OnModelCreating(ModelBuilder b)
  {
    b.HasDefaultSchema("protection");
    b.Entity<UserProtectionRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.Property(x => x.UserId).HasMaxLength(128);
      e.HasIndex(x => x.UserId).IsUnique();
    });
    b.Entity<FreezeConsumptionRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.Property(x => x.UserId).HasMaxLength(128);
      e.HasIndex(x => new { x.UserId, x.Date }).IsUnique(); // one freeze per user-local day
    });
    b.Entity<FreezeAwardRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.Property(x => x.UserId).HasMaxLength(128);
      e.HasIndex(x => new { x.HabitId, x.WeekStart }).IsUnique(); // once ever per (habit, week)
      e.HasIndex(x => x.UserId);
    });
    b.Entity<SettledDayRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.Property(x => x.UserId).HasMaxLength(128);
      e.Property(x => x.Status).HasMaxLength(32);
      e.HasIndex(x => new { x.HabitId, x.Date }).IsUnique();
      e.HasIndex(x => new { x.UserId, x.Date });
    });
  }
}
