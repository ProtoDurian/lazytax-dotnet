namespace Ctx.Friend.Domain;

/// <summary>What another user may see of you: the username only, never email or name.</summary>
public sealed record FriendUser(string UserId, string Username);

public enum FriendRelation
{
  None,
  Friend,
  Outgoing, // you asked them
  Incoming, // they asked you
}

public sealed record FriendCandidate(FriendUser User, FriendRelation Relation);

public sealed record FriendRequestPrincipal(FriendUser From, FriendUser To, DateTime CreatedAtUtc);

public sealed record FriendRequests(IReadOnlyList<FriendRequestPrincipal> Incoming, IReadOnlyList<FriendRequestPrincipal> Outgoing);

public sealed record FriendshipPrincipal(FriendUser Friend, DateTime SinceUtc);

/// <summary>A user's permanent invite link: lazytax.club/f/{Code}. It never expires.</summary>
public sealed record FriendInvitePrincipal(string Code, string UserId);
