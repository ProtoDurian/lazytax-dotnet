using System.ComponentModel.DataAnnotations;
using Ctx.Payment.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Payment.Data;

public sealed class PaymentDb(DbContextOptions<PaymentDb> options) : DbContext(options)
{
  public DbSet<PaymentCustomerRow> PaymentCustomers => this.Set<PaymentCustomerRow>();
  public DbSet<PaymentConsentRow> PaymentConsents => this.Set<PaymentConsentRow>();
  public DbSet<ChargeRow> Charges => this.Set<ChargeRow>();

  protected override void OnModelCreating(ModelBuilder b)
  {
    b.HasDefaultSchema("payment");
    b.Entity<PaymentCustomerRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.HasIndex(x => x.UserId).IsUnique();
      e.HasIndex(x => x.AirwallexCustomerId).IsUnique();
      e.HasMany(x => x.Consents).WithOne(x => x.PaymentCustomer).HasForeignKey(x => x.PaymentCustomerId)
        .OnDelete(DeleteBehavior.Cascade);
    });
    b.Entity<PaymentConsentRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.HasIndex(x => new { x.PaymentCustomerId, x.Purpose }).IsUnique();
    });
    b.Entity<ChargeRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.HasIndex(x => x.UserId);
      e.HasIndex(x => x.IntentId);
      e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
    });
  }
}

/// <summary>LazyTax PaymentCustomers. No foreign key to account; <see cref="UserId"/> is the JWT sub.</summary>
public sealed class PaymentCustomerRow
{
  public Guid Id { get; set; }

  [MaxLength(128)]
  public required string UserId { get; set; }

  [MaxLength(128)]
  public required string AirwallexCustomerId { get; set; }

  public List<PaymentConsentRow> Consents { get; set; } = [];

  public DateTime CreatedAt { get; set; }

  public DateTime UpdatedAt { get; set; }
}

/// <summary>LazyTax PaymentConsents: one purpose-scoped consent, unique per (customer, purpose).</summary>
public sealed class PaymentConsentRow
{
  public Guid Id { get; set; }

  /// <summary><c>ConsentPurpose</c> as int (LazyTax's encoding).</summary>
  public int Purpose { get; set; }

  [MaxLength(128)]
  public required string ConsentId { get; set; }

  [MaxLength(50)]
  public string? Status { get; set; }

  public DateTime CreatedAt { get; set; }

  public DateTime UpdatedAt { get; set; }

  public Guid PaymentCustomerId { get; set; }

  public PaymentCustomerRow? PaymentCustomer { get; set; }
}

/// <summary>New: one logical merchant-initiated charge, keyed by the caller's request id (3-phase state).</summary>
public sealed class ChargeRow
{
  /// <summary>The caller's request id (e.g. a penalty id).</summary>
  public Guid Id { get; set; }

  [MaxLength(128)]
  public required string UserId { get; set; }

  public int Purpose { get; set; }

  public long AmountCents { get; set; }

  [MaxLength(3)]
  public required string Currency { get; set; }

  [MaxLength(500)]
  public string? Description { get; set; }

  public ChargeState State { get; set; }

  public int Attempt { get; set; }

  [MaxLength(128)]
  public string? IntentId { get; set; }

  [MaxLength(50)]
  public string? GatewayStatus { get; set; }

  [MaxLength(500)]
  public string? LastError { get; set; }

  public DateTime? InFlightUntil { get; set; }

  public DateTime CreatedAt { get; set; }

  public DateTime UpdatedAt { get; set; }
}
