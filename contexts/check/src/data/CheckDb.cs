using Microsoft.EntityFrameworkCore;

namespace Ctx.Check.Data;

/// <summary>Values mirror LazyTax HabitCheckStatusData (checks) and HabitExecutionStatusData (executions) for the settled states.</summary>
public enum CheckStatusData : byte
{
  Pending = 0,
  Done = 1,
  Missed = 2,
  Skipped = 3,
  Frozen = 4,
  Vacation = 5,
}

/// <summary>LazyTax HabitChecks, plus a copy of the habit's UserId (no foreign key across contexts).</summary>
public sealed class HabitCheckRow
{
  public Guid Id { get; set; }
  public required Guid HabitId { get; set; }
  public required string UserId { get; set; }
  public required DateOnly LocalDate { get; set; }
  public required string Timezone { get; set; }
  public required DateTime OpensAtUtc { get; set; }
  public required DateTime DeadlineUtc { get; set; }
  public required CheckStatusData Status { get; set; }
  public DateTime? SettledAtUtc { get; set; }
  public string? SettledBy { get; set; }
  public Guid? ExecutionId { get; set; }
}

/// <summary>LazyTax HabitEvidence. (HabitId, ProofKey, ExternalId) is unique: a replay changes nothing.</summary>
public sealed class HabitEvidenceRow
{
  public Guid Id { get; set; }
  public required Guid HabitId { get; set; }
  public required Guid CheckId { get; set; }
  public required string UserId { get; set; }
  public required string ProofKey { get; set; }
  public required string ExternalId { get; set; }
  public required bool Passed { get; set; }
  public required DateTime OccurredAtUtc { get; set; }
  public required DateTime ReceivedAtUtc { get; set; }
  public string? Data { get; set; }
}

/// <summary>
/// LazyTax HabitExecutions. It now keeps copies of HabitId and UserId (LazyTax joined HabitVersions and
/// Habits) and the version's stake at settle time (for the overview's debt). One per habit and date.
/// </summary>
public sealed class HabitExecutionRow
{
  public Guid Id { get; set; }
  public required Guid HabitId { get; set; }
  public required Guid HabitVersionId { get; set; }
  public required string UserId { get; set; }
  public required DateOnly Date { get; set; }
  public required CheckStatusData Status { get; set; }
  public DateTime? CompletedAt { get; set; }
  public string? Notes { get; set; }
  public bool PaymentProcessed { get; set; }
  public long StakeCents { get; set; }
  public int RatioBps { get; set; }
  public string Currency { get; set; } = "USD";
  public Guid? CharityId { get; set; }

  /// <summary>Set when habit says the habit was deleted (LazyTax filtered <c>Habits.DeletedAt == null</c>).</summary>
  public bool HabitDeleted { get; set; }
}

public sealed class CheckDb(DbContextOptions<CheckDb> options) : DbContext(options)
{
  public DbSet<HabitCheckRow> HabitChecks => this.Set<HabitCheckRow>();
  public DbSet<HabitEvidenceRow> HabitEvidence => this.Set<HabitEvidenceRow>();
  public DbSet<HabitExecutionRow> HabitExecutions => this.Set<HabitExecutionRow>();

  protected override void OnModelCreating(ModelBuilder b)
  {
    b.HasDefaultSchema("check");
    b.Entity<HabitCheckRow>(e =>
    {
      e.ToTable("HabitChecks");
      e.HasKey(x => x.Id);
      e.Property(x => x.UserId).HasMaxLength(128);
      e.Property(x => x.Timezone).HasMaxLength(64);
      e.Property(x => x.SettledBy).HasMaxLength(64);
      e.HasIndex(x => new { x.HabitId, x.LocalDate }).IsUnique();
      e.HasIndex(x => new { x.Status, x.DeadlineUtc });
      e.HasIndex(x => x.UserId);
    });
    b.Entity<HabitEvidenceRow>(e =>
    {
      e.ToTable("HabitEvidence");
      e.HasKey(x => x.Id);
      e.Property(x => x.UserId).HasMaxLength(128);
      e.Property(x => x.ProofKey).HasMaxLength(64);
      e.Property(x => x.ExternalId).HasMaxLength(256);
      e.Property(x => x.Data).HasMaxLength(4096);
      e.HasIndex(x => new { x.HabitId, x.ProofKey, x.ExternalId }).IsUnique();
      e.HasIndex(x => x.UserId);
    });
    b.Entity<HabitExecutionRow>(e =>
    {
      e.ToTable("HabitExecutions");
      e.HasKey(x => x.Id);
      e.Property(x => x.UserId).HasMaxLength(128);
      e.Property(x => x.Currency).HasMaxLength(8);
      e.HasIndex(x => new { x.HabitId, x.Date }).IsUnique();
      e.HasIndex(x => new { x.UserId, x.Date });
    });
  }
}
