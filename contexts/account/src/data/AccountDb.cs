using Microsoft.EntityFrameworkCore;

namespace Ctx.Account.Data;

/// <summary>The account context's one DbContext: users and configurations, schema <c>account</c>.</summary>
public sealed class AccountDb(DbContextOptions<AccountDb> options) : DbContext(options)
{
  public DbSet<UserRow> Users => this.Set<UserRow>();

  public DbSet<ConfigurationRow> Configurations => this.Set<ConfigurationRow>();

  protected override void OnModelCreating(ModelBuilder b)
  {
    b.HasDefaultSchema("account");
    b.Entity<UserRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.HasIndex(x => x.Username).IsUnique();
    });
    b.Entity<ConfigurationRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.HasIndex(x => x.UserId).IsUnique(); // one configuration per user
    });
  }
}
