using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Plan.Data;

/// <summary>The plan context's one DbContext, schema <c>plan</c>.</summary>
public sealed class PlanDb(DbContextOptions<PlanDb> options) : DbContext(options)
{
  public DbSet<SubscriptionRow> UserSubscriptions => this.Set<SubscriptionRow>();

  protected override void OnModelCreating(ModelBuilder b)
  {
    b.HasDefaultSchema("plan");
    b.Entity<SubscriptionRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.HasIndex(x => x.UserId).IsUnique(); // one row per user
    });
  }
}

/// <summary>LazyTax's UserSubscriptionData without the payment fields (billing goes external).</summary>
public sealed class SubscriptionRow
{
  public Guid Id { get; set; }

  [MaxLength(128)] public string UserId { get; set; } = string.Empty;

  [MaxLength(32)] public string Tier { get; set; } = string.Empty;

  public int Status { get; set; }

  public DateTime PeriodStart { get; set; }

  public DateTime PeriodEnd { get; set; }

  public bool CancelAtPeriodEnd { get; set; }

  [MaxLength(32)] public string? NextTier { get; set; }

  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
