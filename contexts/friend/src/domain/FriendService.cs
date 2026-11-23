using System.Security.Cryptography;
using Ctx.Friend.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using LazyTax.Platform.Problems;

namespace Ctx.Friend.Domain;

/// <summary>Friend requests, friendships and the permanent invite (LazyTax <c>FriendService</c>).</summary>
public sealed class FriendService(IFriendRepository repo, IUserDirectory users, IUnitOfWork uow, IOutbox outbox)
{
  public const int MinQueryLength = 2;
  public const int MaxSearchLimit = 20;
  public const int InviteCodeLength = 10;

  // Lowercase letters and digits without the look-alikes 0/o, 1/l/i, so a code read aloud or
  // retyped from a screenshot still works.
  public const string InviteAlphabet = "23456789abcdefghjkmnpqrstuvwxyz";

  /// <summary>Usernames starting with the query (case-insensitive), with how each relates to the caller.</summary>
  public async Task<IReadOnlyList<FriendCandidate>> Search(string userId, string query, int limit, CancellationToken ct)
  {
    var q = (query ?? "").Trim();
    if (q.Length < MinQueryLength)
      throw new ValidationFailed("Invalid request", "q", $"Search needs at least {MinQueryLength} characters");
    if (limit is < 1 or > MaxSearchLimit)
      throw new ValidationFailed("Invalid request", "limit", $"Limit must be between 1 and {MaxSearchLimit}");

    var found = await users.Search(userId, q, limit, ct);
    var relations = await repo.Relations(Owner.User(userId), found.Select(x => x.UserId).ToList(), ct);
    return found
      .Select(u => new FriendCandidate(u, relations.GetValueOrDefault(u.UserId, FriendRelation.None)))
      .ToList();
  }

  /// <summary>
  /// Asks <paramref name="toUserId"/> to be friends. If they already asked the caller, this accepts their request
  /// instead. Already friends or already asked: no-op, current relation.
  /// </summary>
  public async Task<FriendRelation> SendRequest(string userId, string toUserId, CancellationToken ct)
  {
    if (userId == toUserId) throw new ValidationFailed("Invalid request", "toUserId", "You can't friend yourself");
    if (await users.Get(userId, ct) is null) throw new NotFound("User", userId);
    if (await users.Get(toUserId, ct) is null) throw new NotFound("User", toUserId);

    return await uow.Start(async () =>
    {
      switch (await repo.Relation(Owner.User(userId), toUserId, ct))
      {
        case FriendRelation.Friend:
          return FriendRelation.Friend;
        case FriendRelation.Outgoing:
          return FriendRelation.Outgoing;
        case FriendRelation.Incoming:
          // They already asked: asking back is accepting.
          await AcceptRequest(userId, toUserId, ct);
          return FriendRelation.Friend;
      }

      await repo.CreateRequest(userId, toUserId, ct);
      await outbox.Add(FriendEvents.RequestSent, new FriendRequestSent(userId, toUserId), ct);
      return FriendRelation.Outgoing;
    }, ct);
  }

  public async Task<FriendRequests> ListRequests(string userId, CancellationToken ct)
  {
    var rows = await repo.ListRequests(Owner.User(userId), ct);
    var names = await users.Usernames(rows.SelectMany(r => new[] { r.FromUserId, r.ToUserId }).Distinct().ToList(), ct);
    FriendRequestPrincipal Of(RequestRow r) => new(
      new FriendUser(r.FromUserId, names.GetValueOrDefault(r.FromUserId, "")),
      new FriendUser(r.ToUserId, names.GetValueOrDefault(r.ToUserId, "")),
      r.CreatedAtUtc);
    return new FriendRequests(
      rows.Where(r => r.ToUserId == userId).Select(Of).ToList(),
      rows.Where(r => r.FromUserId == userId).Select(Of).ToList());
  }

  /// <summary>Accepts <paramref name="fromUserId"/>'s request. Null when there is no such request.</summary>
  public async Task<FriendshipPrincipal?> Accept(string userId, string fromUserId, CancellationToken ct)
  {
    if (await repo.Relation(Owner.User(userId), fromUserId, ct) != FriendRelation.Incoming) return null;
    if (await users.Get(userId, ct) is null) throw new NotFound("User", userId);

    var since = await uow.Start(() => AcceptRequest(userId, fromUserId, ct), ct);
    var other = await users.Get(fromUserId, ct);
    return new FriendshipPrincipal(new FriendUser(fromUserId, other?.Username ?? ""), since);
  }

  /// <summary>Declines an incoming request or cancels an outgoing one, silently. False when none.</summary>
  public Task<bool> DeleteRequest(string userId, string otherUserId, CancellationToken ct) =>
    uow.Start(() => repo.DeleteRequest(Owner.User(userId), otherUserId, ct), ct);

  public async Task<IReadOnlyList<FriendshipPrincipal>> ListFriends(string userId, CancellationToken ct)
  {
    var rows = await repo.ListFriends(Owner.User(userId), ct);
    var names = await users.Usernames(rows.Select(r => r.FriendUserId).ToList(), ct);
    return rows
      .Select(r => new FriendshipPrincipal(new FriendUser(r.FriendUserId, names.GetValueOrDefault(r.FriendUserId, "")), r.SinceUtc))
      .OrderBy(f => f.Friend.Username.ToLowerInvariant(), StringComparer.Ordinal)
      .ThenBy(f => f.Friend.UserId, StringComparer.Ordinal)
      .ToList();
  }

  /// <summary>Ends the friendship both ways, and stops each telling the other about missed habits. False when they weren't friends.</summary>
  public Task<bool> Remove(string userId, string friendUserId, CancellationToken ct) =>
    uow.Start(() => repo.DeleteFriendship(Owner.User(userId), friendUserId, ct), ct);

  /// <summary>The user's one invite: the same code every time, created on the first call.</summary>
  public Task<FriendInvitePrincipal> GetOrCreateInvite(string userId, CancellationToken ct) =>
    uow.Start(() => repo.GetOrCreateInvite(userId, RandomNumberGenerator.GetString(InviteAlphabet, InviteCodeLength), ct), ct);

  /// <summary>
  /// Sends the invite's owner a friend request, exactly as <see cref="SendRequest"/> does: Outgoing, or Friend when
  /// the owner had already asked the caller. Null for an unknown code.
  /// </summary>
  public async Task<FriendRelation?> AcceptInvite(string userId, string code, CancellationToken ct)
  {
    var invite = await repo.GetInvite((code ?? "").Trim().ToLowerInvariant(), ct);
    if (invite is null) return null;
    if (invite.UserId == userId) throw new ValidationFailed("Invalid request", "code", "You can't accept your own invite");

    // The owner still decides: a link only saves searching for their username.
    return await SendRequest(userId, invite.UserId, ct);
  }

  public async Task<bool> AreFriends(string userId, string otherUserId, CancellationToken ct) =>
    await repo.Relation(Owner.User(userId), otherUserId, ct) == FriendRelation.Friend;

  public async Task<IReadOnlyList<string>> ListFriendIds(string userId, CancellationToken ct) =>
    (await repo.ListFriends(Owner.User(userId), ct)).Select(f => f.FriendUserId).ToList();

  private async Task<DateTime> AcceptRequest(string accepterId, string requesterId, CancellationToken ct)
  {
    var since = await repo.CreateFriendship(accepterId, requesterId, ct);
    // The requester is told after commit (the subscriber posts to their inbox).
    await outbox.Add(FriendEvents.Accepted, new FriendAccepted(accepterId, requesterId, since.Ticks), ct);
    return since;
  }
}
