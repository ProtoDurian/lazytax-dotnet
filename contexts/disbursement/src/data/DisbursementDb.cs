using Microsoft.EntityFrameworkCore;

namespace Ctx.Disbursement.Data;

public sealed class DisbursementDb(DbContextOptions<DisbursementDb> options) : DbContext(options)
{
  public DbSet<DisbursementRow> Disbursements => this.Set<DisbursementRow>();

  protected override void OnModelCreating(ModelBuilder b)
  {
    b.HasDefaultSchema("disbursement");
    b.Entity<DisbursementRow>(e =>
    {
      e.ToTable("Disbursements");
      e.HasKey(x => x.Id);
      e.Property(x => x.Currency).HasMaxLength(8);
      e.Property(x => x.Status).HasMaxLength(16);
      e.Property(x => x.PledgeOrganizationId).HasMaxLength(256);
      e.Property(x => x.ProviderDonationId).HasMaxLength(256);
      e.Property(x => x.LastError).HasMaxLength(2048);
      e.HasIndex(x => new { x.Status, x.UpdatedAt });
      e.HasIndex(x => x.CharityId);
    });
  }
}
