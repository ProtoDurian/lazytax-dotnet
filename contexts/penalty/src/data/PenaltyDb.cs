using Microsoft.EntityFrameworkCore;

namespace Ctx.Penalty.Data;

/// <summary>
/// penalty's tables (LazyTax Penalties + CharityBalances). No foreign keys into other schemas: the execution,
/// habit, user, charity and disbursement ids are copies.
/// </summary>
public sealed class PenaltyDb(DbContextOptions<PenaltyDb> options) : DbContext(options)
{
  public DbSet<PenaltyRow> Penalties => this.Set<PenaltyRow>();

  public DbSet<CharityBalanceRow> CharityBalances => this.Set<CharityBalanceRow>();

  protected override void OnModelCreating(ModelBuilder b)
  {
    b.HasDefaultSchema("penalty");
    b.Entity<PenaltyRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
      e.Property(x => x.UserId).HasMaxLength(128);
      e.Property(x => x.Currency).HasMaxLength(8);
      e.Property(x => x.Status).HasMaxLength(16);
      e.Property(x => x.PaymentIntentId).HasMaxLength(256);
      e.Property(x => x.LastError).HasMaxLength(2048);
      // Exactly once per execution: the idempotency key of the check.settled subscriber.
      e.HasIndex(x => x.HabitExecutionId).IsUnique();
      e.HasIndex(x => new { x.UserId, x.CreatedAt });
      // The drain claim: pending (or stale processing), oldest first.
      e.HasIndex(x => new { x.Status, x.CreatedAt });
      // The payout scan and release.
      e.HasIndex(x => new { x.CharityId, x.Currency }).HasFilter("\"Status\" = 'charged' AND \"DisbursementId\" IS NULL");
      e.HasIndex(x => x.DisbursementId).HasFilter("\"DisbursementId\" IS NOT NULL");
      e.ToTable(t =>
      {
        t.HasCheckConstraint("CK_Penalties_AmountCents", "\"AmountCents\" > 0");
        t.HasCheckConstraint("CK_Penalties_Status",
          "\"Status\" IN ('pending', 'processing', 'charged', 'failed', 'skipped')");
      });
    });
    b.Entity<CharityBalanceRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
      e.Property(x => x.Currency).HasMaxLength(8);
      // One row per charity per currency: money is never summed across currencies.
      e.HasIndex(x => new { x.CharityId, x.Currency }).IsUnique();
    });
  }
}

public sealed class PenaltyRow
{
  public Guid Id { get; set; }
  public Guid HabitExecutionId { get; set; }
  public Guid HabitId { get; set; }
  public required string UserId { get; set; }
  public Guid CharityId { get; set; }
  public long AmountCents { get; set; }
  public required string Currency { get; set; }
  public required string Status { get; set; }
  public string? PaymentIntentId { get; set; }
  public int Attempts { get; set; }
  public string? LastError { get; set; }

  /// <summary>NULL while charged = pending payout; set once a disbursement claimed it.</summary>
  public Guid? DisbursementId { get; set; }

  public DateTime CreatedAt { get; set; }
  public DateTime UpdatedAt { get; set; }
}

public sealed class CharityBalanceRow
{
  public Guid Id { get; set; }
  public Guid CharityId { get; set; }
  public required string Currency { get; set; }
  public long AccruedCents { get; set; }
  public DateTime UpdatedAt { get; set; }
}
