using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Charity.Data;

public sealed class CharityDb(DbContextOptions<CharityDb> options) : DbContext(options)
{
  public DbSet<CharityRow> Charities => this.Set<CharityRow>();
  public DbSet<CauseRow> Causes => this.Set<CauseRow>();
  public DbSet<CharityCauseRow> CharityCauses => this.Set<CharityCauseRow>();
  public DbSet<ExternalIdRow> ExternalIds => this.Set<ExternalIdRow>();

  protected override void OnModelCreating(ModelBuilder b)
  {
    b.HasDefaultSchema("charity");
    b.Entity<CharityRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.Property(x => x.Id).ValueGeneratedOnAdd();
      e.HasIndex(x => x.Name);
      e.HasIndex(x => new { x.PrimaryRegistrationCountry, x.PrimaryRegistrationNumber });
      e.HasIndex(x => x.Countries).HasMethod("gin");
    });
    b.Entity<CauseRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.Property(x => x.Id).ValueGeneratedOnAdd();
      e.HasIndex(x => x.Key).IsUnique();
    });
    b.Entity<CharityCauseRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.Property(x => x.Id).ValueGeneratedOnAdd();
      e.HasIndex(x => new { x.CharityId, x.CauseId }).IsUnique();
      e.HasOne<CharityRow>().WithMany().HasForeignKey(x => x.CharityId).OnDelete(DeleteBehavior.Cascade);
      e.HasOne<CauseRow>().WithMany().HasForeignKey(x => x.CauseId).OnDelete(DeleteBehavior.Cascade);
    });
    b.Entity<ExternalIdRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.Property(x => x.Id).ValueGeneratedOnAdd();
      e.HasIndex(x => new { x.Source, x.ExternalKey }).IsUnique();
      e.HasOne<CharityRow>().WithMany().HasForeignKey(x => x.CharityId).OnDelete(DeleteBehavior.Cascade);
    });
  }
}

public sealed class CharityRow
{
  public Guid Id { get; set; }
  [MaxLength(256)] public required string Name { get; set; }
  [MaxLength(128)] public string? Slug { get; set; }
  [MaxLength(8192)] public string? Mission { get; set; }
  public string[] Countries { get; set; } = [];
  [MaxLength(128)] public string? PrimaryRegistrationNumber { get; set; }
  [MaxLength(2)] public string? PrimaryRegistrationCountry { get; set; }
  [MaxLength(512)] public string? WebsiteUrl { get; set; }
  [MaxLength(512)] public string? LogoUrl { get; set; }
}

public sealed class CauseRow
{
  public Guid Id { get; set; }
  [MaxLength(128)] public required string Key { get; set; }
  [MaxLength(256)] public required string Name { get; set; }
}

public sealed class CharityCauseRow
{
  public Guid Id { get; set; }
  public Guid CharityId { get; set; }
  public Guid CauseId { get; set; }
}

public sealed class ExternalIdRow
{
  public Guid Id { get; set; }
  public Guid CharityId { get; set; }
  [MaxLength(64)] public required string Source { get; set; }
  [MaxLength(256)] public required string ExternalKey { get; set; }
  [MaxLength(2048)] public string? Url { get; set; }
  public string? Payload { get; set; }
  public DateTimeOffset? LastSyncedAt { get; set; }
}
