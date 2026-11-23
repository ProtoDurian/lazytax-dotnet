namespace Ctx.Friend.Contract.V1;

/// <summary>Outbox event names emitted by friend.</summary>
public static class FriendEvents
{
  /// <summary>
  /// A friend request was sent (payload <see cref="FriendRequestSent"/>). friend's own subscriber posts the inbox
  /// notification after commit (LazyTax posted it best effort after the write).
  /// </summary>
  public const string RequestSent = "friend.request_sent";

  /// <summary>A friend request was accepted (payload <see cref="FriendAccepted"/>); the requester is told.</summary>
  public const string Accepted = "friend.accepted";
}

public sealed record FriendRequestSent(string FromUserId, string ToUserId);

/// <summary><paramref name="SinceTicks"/> is the friendship's <c>CreatedAtUtc.Ticks</c>, used as the inbox dedupe key.</summary>
public sealed record FriendAccepted(string AccepterUserId, string RequesterUserId, long SinceTicks);
