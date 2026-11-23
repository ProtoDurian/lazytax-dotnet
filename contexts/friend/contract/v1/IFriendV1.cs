namespace Ctx.Friend.Contract.V1;

/// <summary>friend.v1: who is friends with whom, and who is told when a habit is missed.</summary>
public interface IFriendV1
{
  /// <summary>True when <paramref name="userId"/> and <paramref name="otherUserId"/> are friends (LazyTax <c>IFriendService.AreFriends</c>).</summary>
  Task<bool> AreFriends(string userId, string otherUserId, CancellationToken ct = default);

  /// <summary>The ids of the user's friends, by username.</summary>
  Task<IReadOnlyList<string>> ListFriendIds(string userId, CancellationToken ct = default);

  /// <summary>
  /// The friends told when the habit's day ends missed: the habit owner's watchers who are still their friends.
  /// <paramref name="ownerUserId"/> is a trusted caller's decision (the habit's owner).
  /// </summary>
  Task<IReadOnlyList<string>> ListWatcherIds(string ownerUserId, Guid habitId, CancellationToken ct = default);
}
