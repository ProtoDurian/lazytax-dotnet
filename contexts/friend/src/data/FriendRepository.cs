using Ctx.Friend.Domain;
using LazyTax.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Friend.Data;

public sealed class FriendRepository(FriendDb db) : IFriendRepository
{
  public async Task<FriendRelation> Relation(Owner owner, string otherUserId, CancellationToken ct)
  {
    var me = owner.UserId!;
    var friend = await db.Friendships.AsNoTracking().OwnedBy(owner, x => x.UserId)
      .AnyAsync(x => x.FriendUserId == otherUserId, ct);
    var incoming = await db.FriendRequests.AsNoTracking()
      .AnyAsync(x => x.FromUserId == otherUserId && x.ToUserId == me, ct);
    var outgoing = await db.FriendRequests.AsNoTracking().OwnedBy(owner, x => x.FromUserId)
      .AnyAsync(x => x.ToUserId == otherUserId, ct);
    return RelationOf(friend, incoming, outgoing);
  }

  public async Task<IReadOnlyDictionary<string, FriendRelation>> Relations(
    Owner owner, IReadOnlyCollection<string> ids, CancellationToken ct)
  {
    var me = owner.UserId!;
    var friends = (await db.Friendships.AsNoTracking().OwnedBy(owner, x => x.UserId)
      .Where(x => ids.Contains(x.FriendUserId)).Select(x => x.FriendUserId).ToListAsync(ct)).ToHashSet();
    var incoming = (await db.FriendRequests.AsNoTracking()
      .Where(x => x.ToUserId == me && ids.Contains(x.FromUserId)).Select(x => x.FromUserId).ToListAsync(ct)).ToHashSet();
    var outgoing = (await db.FriendRequests.AsNoTracking().OwnedBy(owner, x => x.FromUserId)
      .Where(x => ids.Contains(x.ToUserId)).Select(x => x.ToUserId).ToListAsync(ct)).ToHashSet();
    return ids.Distinct().ToDictionary(id => id, id => RelationOf(friends.Contains(id), incoming.Contains(id), outgoing.Contains(id)));
  }

  // ON CONFLICT DO NOTHING: a concurrent send of the same request won, and it is already there. A unique
  // violation would abort the whole transaction.
  public async Task CreateRequest(string fromUserId, string toUserId, CancellationToken ct) =>
    await db.Database.ExecuteSqlInterpolatedAsync(
      $"""
       INSERT INTO friend."FriendRequests" ("Id", "FromUserId", "ToUserId", "CreatedAtUtc")
       VALUES ({Guid.NewGuid()}, {fromUserId}, {toUserId}, {DateTime.UtcNow})
       ON CONFLICT ("FromUserId", "ToUserId") DO NOTHING
       """, ct);

  public async Task<IReadOnlyList<RequestRow>> ListRequests(Owner owner, CancellationToken ct)
  {
    var me = owner.UserId!;
    return await db.FriendRequests.AsNoTracking()
      .Where(x => x.ToUserId == me || x.FromUserId == me)
      .OrderByDescending(x => x.CreatedAtUtc)
      .Select(x => new RequestRow(x.FromUserId, x.ToUserId, x.CreatedAtUtc))
      .ToListAsync(ct);
  }

  public async Task<bool> DeleteRequest(Owner owner, string otherUserId, CancellationToken ct)
  {
    var me = owner.UserId!;
    var affected = await db.FriendRequests
      .Where(x => (x.FromUserId == me && x.ToUserId == otherUserId) || (x.FromUserId == otherUserId && x.ToUserId == me))
      .ExecuteDeleteAsync(ct);
    return affected > 0;
  }

  public async Task<DateTime> CreateFriendship(string userId, string friendUserId, CancellationToken ct)
  {
    var now = DateTime.UtcNow;
    // Both directions in one statement; a concurrent accept's rows win and keep their own timestamp.
    await db.Database.ExecuteSqlInterpolatedAsync(
      $"""
       INSERT INTO friend."Friendships" ("Id", "UserId", "FriendUserId", "CreatedAtUtc")
       VALUES ({Guid.NewGuid()}, {userId}, {friendUserId}, {now}), ({Guid.NewGuid()}, {friendUserId}, {userId}, {now})
       ON CONFLICT ("UserId", "FriendUserId") DO NOTHING
       """, ct);
    await db.FriendRequests
      .Where(x => (x.FromUserId == userId && x.ToUserId == friendUserId) || (x.FromUserId == friendUserId && x.ToUserId == userId))
      .ExecuteDeleteAsync(ct);
    return await db.Friendships.AsNoTracking()
      .Where(x => x.UserId == userId && x.FriendUserId == friendUserId)
      .Select(x => x.CreatedAtUtc).SingleAsync(ct);
  }

  public async Task<IReadOnlyList<Domain.FriendshipRow>> ListFriends(Owner owner, CancellationToken ct) =>
    await db.Friendships.AsNoTracking().OwnedBy(owner, x => x.UserId)
      .Select(x => new Domain.FriendshipRow(x.FriendUserId, x.CreatedAtUtc))
      .ToListAsync(ct);

  public async Task<bool> DeleteFriendship(Owner owner, string friendUserId, CancellationToken ct)
  {
    var me = owner.UserId!;
    var removed = await db.Friendships
      .Where(x => (x.UserId == me && x.FriendUserId == friendUserId) || (x.UserId == friendUserId && x.FriendUserId == me))
      .ExecuteDeleteAsync(ct);
    // Each stops watching the other's habits (OwnerUserId is the habit's owner).
    await db.HabitFriendWatchers
      .Where(x => (x.OwnerUserId == me && x.FriendUserId == friendUserId) || (x.OwnerUserId == friendUserId && x.FriendUserId == me))
      .ExecuteDeleteAsync(ct);
    return removed > 0;
  }

  public async Task<FriendInvitePrincipal> GetOrCreateInvite(string userId, string newCode, CancellationToken ct)
  {
    // A concurrent first call (or a code collision) inserts nothing; everyone reads back the user's one code.
    await db.Database.ExecuteSqlInterpolatedAsync(
      $"""
       INSERT INTO friend."FriendInvites" ("Code", "UserId", "CreatedAtUtc")
       VALUES ({newCode}, {userId}, {DateTime.UtcNow})
       ON CONFLICT DO NOTHING
       """, ct);
    var row = await db.FriendInvites.AsNoTracking().Where(x => x.UserId == userId).FirstAsync(ct);
    return new FriendInvitePrincipal(row.Code, row.UserId);
  }

  public async Task<FriendInvitePrincipal?> GetInvite(string code, CancellationToken ct)
  {
    var row = await db.FriendInvites.AsNoTracking().Where(x => x.Code == code).FirstOrDefaultAsync(ct);
    return row is null ? null : new FriendInvitePrincipal(row.Code, row.UserId);
  }

  // A friendship wins over requests; an incoming request wins over an outgoing one (when both exist after a
  // race), so the caller is offered the accept.
  private static FriendRelation RelationOf(bool friend, bool incoming, bool outgoing) =>
    friend ? FriendRelation.Friend
    : incoming ? FriendRelation.Incoming
    : outgoing ? FriendRelation.Outgoing
    : FriendRelation.None;
}
