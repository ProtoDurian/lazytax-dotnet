using Ctx.Friend.Contract.V1;
using Ctx.Friend.Domain;
using LazyTax.Platform.Data;

namespace Ctx.Friend;

/// <summary>friend.v1 over the friend services.</summary>
public sealed class FriendV1(FriendService friends, IFriendStakeRepository stake) : IFriendV1
{
  public Task<bool> AreFriends(string userId, string otherUserId, CancellationToken ct = default) =>
    friends.AreFriends(userId, otherUserId, ct);

  public Task<IReadOnlyList<string>> ListFriendIds(string userId, CancellationToken ct = default) =>
    friends.ListFriendIds(userId, ct);

  public Task<IReadOnlyList<string>> ListWatcherIds(string ownerUserId, Guid habitId, CancellationToken ct = default) =>
    stake.GetWatcherIds(Owner.User(ownerUserId), habitId, ct);
}
