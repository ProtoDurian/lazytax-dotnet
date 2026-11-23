using Ctx.Friend.Domain;

namespace Ctx.Friend.Api.V1;

public sealed record SendFriendRequestReq(string? ToUserId);

/// <summary>Relation, lowercase: none | friend | outgoing (you asked them) | incoming (they asked you).</summary>
public sealed record FriendCandidateRes(string UserId, string Username, string Relation);

public sealed record FriendRelationRes(string Relation);

public sealed record FriendRes(string UserId, string Username, DateTime SinceUtc);

/// <summary>UserId and Username are the other person: the sender for incoming, the recipient for outgoing.</summary>
public sealed record FriendRequestRes(string UserId, string Username, DateTime CreatedAtUtc);

public sealed record FriendRequestsRes(List<FriendRequestRes> Incoming, List<FriendRequestRes> Outgoing);

/// <summary>The app builds the link: lazytax.club/f/{Code}. It never expires.</summary>
public sealed record FriendInviteRes(string Code);

/// <summary>The friends to tell when the habit's day ends missed; an empty list turns it off.</summary>
public sealed record SetFriendWatchersReq(List<string>? FriendUserIds);

public sealed record FriendWatcherRes(string UserId, string Username);

public sealed record FriendStakeRes(Guid HabitId, List<FriendWatcherRes> Friends);

public static class FriendMappers
{
  public static string ToRes(this FriendRelation r) => r.ToString().ToLowerInvariant();

  public static FriendCandidateRes ToRes(this FriendCandidate c) => new(c.User.UserId, c.User.Username, c.Relation.ToRes());

  public static FriendRes ToRes(this FriendshipPrincipal f) => new(f.Friend.UserId, f.Friend.Username, f.SinceUtc);

  public static FriendInviteRes ToRes(this FriendInvitePrincipal i) => new(i.Code);

  public static FriendRequestsRes ToRes(this FriendRequests r) => new(
    [.. r.Incoming.Select(x => new FriendRequestRes(x.From.UserId, x.From.Username, x.CreatedAtUtc))],
    [.. r.Outgoing.Select(x => new FriendRequestRes(x.To.UserId, x.To.Username, x.CreatedAtUtc))]);

  public static FriendStakeRes ToRes(this IReadOnlyList<FriendUser> w, Guid habitId) =>
    new(habitId, [.. w.Select(x => new FriendWatcherRes(x.UserId, x.Username))]);
}
