using Ctx.Friend.Domain;
using LazyTax.Platform.Http;
using LazyTax.Platform.Problems;
using Microsoft.AspNetCore.Mvc;

namespace Ctx.Friend.Api.V1;

[Route("Friend")]
public sealed class FriendController(FriendService service) : ContextController
{
  /// <summary>Usernames starting with q (case-insensitive); never email or name.</summary>
  [Policy(Policies.Registered), Subject("userId")]
  [HttpGet("{userId}/search")]
  public async Task<List<FriendCandidateRes>> Search(string userId, [FromQuery] string? q,
    CancellationToken ct, [FromQuery] int limit = FriendService.MaxSearchLimit) =>
    [.. (await service.Search(userId, q ?? "", limit, ct)).Select(x => x.ToRes())];

  [Policy(Policies.Registered), Subject("userId")]
  [HttpGet("{userId}")]
  public async Task<List<FriendRes>> ListFriends(string userId, CancellationToken ct) =>
    [.. (await service.ListFriends(userId, ct)).Select(x => x.ToRes())];

  [Policy(Policies.Registered), Subject("userId")]
  [HttpDelete("{userId}/{friendUserId}")]
  public async Task<IActionResult> Remove(string userId, string friendUserId, CancellationToken ct)
  {
    if (!await service.Remove(userId, friendUserId, ct)) throw new NotFound("Friend", friendUserId);
    return this.NoContent();
  }

  [Policy(Policies.Registered), Subject("userId")]
  [HttpGet("{userId}/requests")]
  public async Task<FriendRequestsRes> ListRequests(string userId, CancellationToken ct) =>
    (await service.ListRequests(userId, ct)).ToRes();

  /// <summary>Asking someone who already asked you accepts their request (relation "friend").</summary>
  [Policy(Policies.Registered), Subject("userId")]
  [HttpPost("{userId}/requests")]
  public async Task<FriendRelationRes> SendRequest(string userId, [FromBody] SendFriendRequestReq req, CancellationToken ct)
  {
    if (string.IsNullOrEmpty(req.ToUserId) || req.ToUserId.Length > 128)
      throw new ValidationFailed("Invalid SendFriendRequestReq", "toUserId", "ToUserId is required and at most 128 characters");
    return new FriendRelationRes((await service.SendRequest(userId, req.ToUserId, ct)).ToRes());
  }

  [Policy(Policies.Registered), Subject("userId")]
  [HttpPost("{userId}/requests/{fromUserId}/accept")]
  public async Task<FriendRes> Accept(string userId, string fromUserId, CancellationToken ct) =>
    (await service.Accept(userId, fromUserId, ct))?.ToRes() ?? throw new NotFound("Friend Request", fromUserId);

  /// <summary>Declines an incoming request or cancels an outgoing one.</summary>
  [Policy(Policies.Registered), Subject("userId")]
  [HttpDelete("{userId}/requests/{otherUserId}")]
  public async Task<IActionResult> DeleteRequest(string userId, string otherUserId, CancellationToken ct)
  {
    if (!await service.DeleteRequest(userId, otherUserId, ct)) throw new NotFound("Friend Request", otherUserId);
    return this.NoContent();
  }

  /// <summary>The user's permanent invite: the same code on every call.</summary>
  [Policy(Policies.Registered), Subject("userId")]
  [HttpPost("{userId}/invites")]
  public async Task<FriendInviteRes> CreateInvite(string userId, CancellationToken ct) =>
    (await service.GetOrCreateInvite(userId, ct)).ToRes();

  /// <summary>Sends the code's owner a friend request, like POST requests: "outgoing", or "friend" when the owner had already asked you.</summary>
  [Policy(Policies.Registered), Subject("userId")]
  [HttpPost("{userId}/invites/{code}/accept")]
  public async Task<FriendRelationRes> AcceptInvite(string userId, string code, CancellationToken ct) =>
    await service.AcceptInvite(userId, code, ct) is { } relation
      ? new FriendRelationRes(relation.ToRes())
      : throw new NotFound("Friend Invite", code);
}
