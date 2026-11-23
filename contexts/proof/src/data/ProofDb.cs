using Microsoft.EntityFrameworkCore;

namespace Ctx.Proof.Data;

public sealed class ProofDb(DbContextOptions<ProofDb> options) : DbContext(options)
{
  public DbSet<ProofBindingRow> ProofBindings => this.Set<ProofBindingRow>();

  public DbSet<NfcTagRow> NfcTags => this.Set<NfcTagRow>();

  public DbSet<StravaConnectionRow> StravaConnections => this.Set<StravaConnectionRow>();

  public DbSet<StravaEventRow> StravaEvents => this.Set<StravaEventRow>();

  protected override void OnModelCreating(ModelBuilder b)
  {
    b.HasDefaultSchema("proof"); // REQUIRED, first line; the app refuses another schema
    b.Entity<ProofBindingRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.HasIndex(x => x.HabitId).IsUnique(); // one proof per habit
      e.HasIndex(x => new { x.UserId, x.PluginKey });
    });
    b.Entity<NfcTagRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.HasIndex(x => x.UserId);
    });
    b.Entity<StravaConnectionRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.HasIndex(x => x.UserId).IsUnique();
      e.HasIndex(x => x.AthleteId).IsUnique();
    });
    b.Entity<StravaEventRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.HasIndex(x => x.ReceivedAtUtc);
      e.HasIndex(x => x.OwnerId);
    });
  }
}
