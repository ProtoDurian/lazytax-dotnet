using Ctx.Friend.Domain;
using LazyTax.Platform.Http;
using LazyTax.Platform.Problems;
using Microsoft.AspNetCore.Mvc;

namespace Ctx.Friend.Api.V1;

/// <summary>"Tell my friends if I miss": which friends hear when a habit's day ends missed.</summary>
[Route("FriendStake")]
public sealed class FriendStakeController(FriendStakeService service) : ContextController
{
  [Policy(Policies.Registered), Subject("userId")]
  [HttpGet("{userId}/{habitId:guid}")]
  public async Task<FriendStakeRes> Get(string userId, Guid habitId, CancellationToken ct) =>
    (await service.GetWatchers(userId, habitId, ct)).ToRes(habitId);

  /// <summary>Replaces the habit's watchers; an empty list turns it off.</summary>
  [Policy(Policies.Registered), Subject("userId")]
  [HttpPut("{userId}/{habitId:guid}")]
  public async Task<FriendStakeRes> Set(string userId, Guid habitId, [FromBody] SetFriendWatchersReq req, CancellationToken ct)
  {
    if (req.FriendUserIds is null) throw new ValidationFailed("Invalid SetFriendWatchersReq", "friendUserIds", "FriendUserIds is required");
    if (req.FriendUserIds.Any(id => string.IsNullOrEmpty(id) || id.Length > 128))
      throw new ValidationFailed("Invalid SetFriendWatchersReq", "friendUserIds", "Each id is required and at most 128 characters");
    return (await service.SetWatchers(userId, habitId, req.FriendUserIds, ct)).ToRes(habitId);
  }
}
