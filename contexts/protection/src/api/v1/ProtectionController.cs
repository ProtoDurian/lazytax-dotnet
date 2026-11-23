using Ctx.Protection.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ctx.Protection.Api.V1;

public sealed record ProtectionBalanceRes(string UserId, int Balance, int Cap);

public sealed record AwardWeeklyReq(string UserId);

public sealed record AwardWeeklyRes(string UserId, int Awards);

[Route("")]
public sealed class ProtectionController(ProtectionService service) : ContextController
{
  /// <summary>Reading the balance cuts it to the current cap first, so Balance is never above Cap.</summary>
  [Policy(Policies.Registered), Subject("userId")]
  [HttpGet("{userId}")]
  public async Task<ProtectionBalanceRes> GetBalance(string userId, CancellationToken ct)
  {
    var b = await service.Balance(Owner.User(userId), ct);
    return new ProtectionBalanceRes(b.UserId, b.Balance, b.Cap);
  }

  /// <summary>Event-driven trigger: an admin can award a user's weekly freezes now.</summary>
  [Policy(Policies.Admin)]
  [HttpPost("award-weekly")]
  public async Task<AwardWeeklyRes> AwardWeekly([FromBody] AwardWeeklyReq req, CancellationToken ct) =>
    new(req.UserId, await service.AwardWeekly(req.UserId, null, ct));
}
