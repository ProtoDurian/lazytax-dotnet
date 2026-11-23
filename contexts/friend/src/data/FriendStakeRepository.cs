using Ctx.Friend.Domain;
using LazyTax.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Friend.Data;

public sealed class FriendStakeRepository(FriendDb db) : IFriendStakeRepository
{
  // Only watchers still friends with the habit's owner: a stale row of a removed friend never tells them anything.
  public async Task<IReadOnlyList<string>> GetWatcherIds(Owner owner, Guid habitId, CancellationToken ct) =>
    await (from w in db.HabitFriendWatchers.AsNoTracking().OwnedBy(owner, x => x.OwnerUserId)
           join f in db.Friendships.AsNoTracking()
             on new { UserId = w.OwnerUserId, w.FriendUserId } equals new { f.UserId, f.FriendUserId }
           where w.HabitId == habitId
           select w.FriendUserId).ToListAsync(ct);

  public async Task ReplaceWatchers(string ownerUserId, Guid habitId, IReadOnlyCollection<string> friendUserIds, CancellationToken ct)
  {
    await db.HabitFriendWatchers.Where(w => w.HabitId == habitId && w.OwnerUserId == ownerUserId).ExecuteDeleteAsync(ct);
    db.HabitFriendWatchers.AddRange(friendUserIds.Distinct()
      .Select(id => new WatcherRow { Id = Guid.NewGuid(), HabitId = habitId, OwnerUserId = ownerUserId, FriendUserId = id }));
    await db.SaveChangesAsync(ct);
    db.ChangeTracker.Clear();
  }

  public async Task DeleteForHabits(string ownerUserId, IReadOnlyCollection<Guid> habitIds, CancellationToken ct) =>
    await db.HabitFriendWatchers.Where(w => w.OwnerUserId == ownerUserId && habitIds.Contains(w.HabitId)).ExecuteDeleteAsync(ct);
}
