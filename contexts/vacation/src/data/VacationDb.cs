using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Vacation.Data;

/// <summary>The vacation context's one DbContext, schema <c>vacation</c>.</summary>
public sealed class VacationDb(DbContextOptions<VacationDb> options) : DbContext(options)
{
  public DbSet<VacationPeriodRow> VacationPeriods => this.Set<VacationPeriodRow>();

  protected override void OnModelCreating(ModelBuilder b)
  {
    b.HasDefaultSchema("vacation");
    b.Entity<VacationPeriodRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.HasIndex(x => new { x.UserId, x.StartDate });
    });
  }
}

/// <summary>LazyTax's VacationPeriodData. UserId is a copy of the account id: no cross-context foreign key.</summary>
public sealed class VacationPeriodRow
{
  public Guid Id { get; set; }

  [MaxLength(128)] public string UserId { get; set; } = string.Empty;

  public DateOnly StartDate { get; set; }

  public DateOnly EndDate { get; set; }

  [MaxLength(256)] public string Timezone { get; set; } = string.Empty;

  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
