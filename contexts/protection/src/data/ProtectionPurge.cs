using LazyTax.Platform.Lifecycle;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Protection.Data;

/// <summary>Account deletion: removes every row of the user from the four protection tables.</summary>
public sealed class ProtectionPurge(ProtectionDb db) : IUserPurge
{
  public async Task Purge(string userId, CancellationToken ct)
  {
    await db.UserProtections.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
    await db.FreezeConsumptions.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
    await db.FreezeAwards.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
    await db.SettledDays.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
  }
}
