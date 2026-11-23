using LazyTax.Platform.Lifecycle;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Friend.Data;

/// <summary>Account deletion: every row where the user is either side.</summary>
public sealed class FriendPurge(FriendDb db) : IUserPurge
{
  public async Task Purge(string userId, CancellationToken ct)
  {
    await db.FriendRequests.Where(x => x.FromUserId == userId || x.ToUserId == userId).ExecuteDeleteAsync(ct);
    await db.Friendships.Where(x => x.UserId == userId || x.FriendUserId == userId).ExecuteDeleteAsync(ct);
    await db.FriendInvites.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
    await db.HabitFriendWatchers.Where(x => x.OwnerUserId == userId || x.FriendUserId == userId).ExecuteDeleteAsync(ct);
  }
}
