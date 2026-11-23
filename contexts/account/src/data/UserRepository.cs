using Ctx.Account.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ctx.Account.Data;

public sealed class UserRepository(AccountDb db) : IUserRepository
{
  public async Task<IReadOnlyList<UserPrincipal>> Search(UserSearch s, CancellationToken ct)
  {
    var q = db.Users.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(s.Username)) q = q.Where(x => EF.Functions.ILike(x.Username, $"%{s.Username}%"));
    if (!string.IsNullOrWhiteSpace(s.Id)) q = q.Where(x => EF.Functions.ILike(x.Id, $"%{s.Id}%"));
    if (!string.IsNullOrEmpty(s.Email)) q = q.Where(x => EF.Functions.ILike(x.Email, $"%{s.Email}%"));
    if (s.EmailVerified is { } ev) q = q.Where(x => x.EmailVerified == ev);
    if (s.Active is { } a) q = q.Where(x => x.Active == a);
    var rows = await q.OrderBy(x => x.Id).Skip(s.Skip).Take(s.Limit).ToListAsync(ct);
    return [.. rows.Select(ToPrincipal)];
  }

  public async Task<UserPrincipal?> Get(Owner owner, string id, CancellationToken ct)
  {
    var row = await db.Users.AsNoTracking().OwnedBy(owner, x => x.Id).FirstOrDefaultAsync(x => x.Id == id, ct);
    return row is null ? null : ToPrincipal(row);
  }

  public async Task<UserPrincipal?> GetByUsername(Owner owner, string username, CancellationToken ct)
  {
    var row = await db.Users.AsNoTracking().OwnedBy(owner, x => x.Id).FirstOrDefaultAsync(x => x.Username == username, ct);
    return row is null ? null : ToPrincipal(row);
  }

  public async Task<UserPrincipal> Create(string id, UserRecord r, CancellationToken ct)
  {
    var row = new UserRow { Id = id };
    Apply(row, r);
    db.Users.Add(row);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
    {
      db.ChangeTracker.Clear();
      throw new Conflict("Failed to create User due to conflicting with existing record");
    }
    return ToPrincipal(row);
  }

  public async Task<UserPrincipal?> Update(Owner owner, string id, UserRecord r, CancellationToken ct)
  {
    var row = await db.Users.OwnedBy(owner, x => x.Id).FirstOrDefaultAsync(x => x.Id == id, ct);
    if (row is null) return null;
    Apply(row, r);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
    {
      db.ChangeTracker.Clear();
      throw new Conflict("Failed to update User due to conflicting with existing record");
    }
    return ToPrincipal(row);
  }

  public Task<bool> Exists(string id, CancellationToken ct) => db.Users.AnyAsync(x => x.Id == id, ct);

  private static void Apply(UserRow row, UserRecord r)
  {
    row.Username = r.Username;
    row.Email = r.Email;
    row.EmailVerified = r.EmailVerified;
    row.Active = r.Active;
    row.Scopes = r.Scopes;
  }

  private static UserPrincipal ToPrincipal(UserRow x) =>
    new(x.Id, new UserRecord(x.Username, x.Email, x.EmailVerified, x.Active, x.Scopes));
}
