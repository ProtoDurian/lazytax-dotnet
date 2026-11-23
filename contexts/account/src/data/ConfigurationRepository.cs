using Ctx.Account.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ctx.Account.Data;

public sealed class ConfigurationRepository(AccountDb db) : IConfigurationRepository
{
  public async Task<ConfigurationPrincipal?> Get(Owner owner, Guid id, CancellationToken ct)
  {
    var row = await db.Configurations.AsNoTracking().OwnedBy(owner, x => x.UserId).FirstOrDefaultAsync(x => x.Id == id, ct);
    return row is null ? null : ToPrincipal(row);
  }

  public async Task<ConfigurationPrincipal?> GetByUser(Owner owner, CancellationToken ct)
  {
    if (owner.IsAnyone) throw new ArgumentException("GetByUser needs a user owner", nameof(owner));
    var row = await db.Configurations.AsNoTracking().OwnedBy(owner, x => x.UserId).FirstOrDefaultAsync(ct);
    return row is null ? null : ToPrincipal(row);
  }

  public async Task<ConfigurationPrincipal> Create(string userId, ConfigurationRecord r, CancellationToken ct)
  {
    var row = new ConfigurationRow { Id = Guid.NewGuid(), UserId = userId, Timezone = r.Timezone, DefaultCharityId = r.DefaultCharityId };
    db.Configurations.Add(row);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
    {
      // One configuration per user: a second create (two first sign-ins at once) conflicts.
      db.ChangeTracker.Clear();
      throw new Conflict("Configuration already exists for this user");
    }
    return ToPrincipal(row);
  }

  public async Task<ConfigurationPrincipal?> Update(Owner owner, Guid id, ConfigurationRecord r, CancellationToken ct)
  {
    var row = await db.Configurations.OwnedBy(owner, x => x.UserId).FirstOrDefaultAsync(x => x.Id == id, ct);
    if (row is null) return null;
    row.Timezone = r.Timezone;
    row.DefaultCharityId = r.DefaultCharityId;
    await db.SaveChangesAsync(ct);
    return ToPrincipal(row);
  }

  private static ConfigurationPrincipal ToPrincipal(ConfigurationRow x) =>
    new(x.Id, x.UserId, new ConfigurationRecord(x.Timezone, x.DefaultCharityId));
}
