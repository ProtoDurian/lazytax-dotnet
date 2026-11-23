using LazyTax.Platform.Lifecycle;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Account.Data;

/// <summary>Deletes the user's own account rows (configuration, then the user) inside the purge unit of work.</summary>
public sealed class AccountPurge(AccountDb db) : IUserPurge
{
  public async Task Purge(string userId, CancellationToken ct)
  {
    await db.Configurations.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
    await db.Users.Where(x => x.Id == userId).ExecuteDeleteAsync(ct);
  }
}
