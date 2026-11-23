using Ctx.Plan.Domain;
using LazyTax.Platform.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ctx.Plan.Api.V1;

/// <summary>LazyTax's SubscriptionController paths (/api/v1/Subscription/...). Billing routes are not ported.</summary>
[Route("")]
public sealed class SubscriptionController(PlanService service, Catalog catalog) : ContextController
{
  /// <summary>Public plan catalog, cheapest first. The literal "plans" wins over {userId}.</summary>
  [Policy(Policies.Public), HttpGet("plans")]
  public IEnumerable<PlanRes> Plans() => catalog.All().Select(p => p.ToRes());

  [Policy(Policies.Registered), Subject("userId"), HttpGet("{userId}")]
  public async Task<SubscriptionRes> Get(string userId, CancellationToken ct) =>
    (await service.Effective(userId, ct)).ToRes(userId);

  [Policy(Policies.Registered), Subject("userId"), HttpPost("{userId}/cancel")]
  public async Task<SubscriptionRes> Cancel(string userId, CancellationToken ct)
  {
    await service.Cancel(userId, ct);
    return (await service.Effective(userId, ct)).ToRes(userId);
  }

  /// <summary>Admin only: set the tier (replaces subscribe/charge until billing exists).</summary>
  [Policy(Policies.Admin), HttpPut("{userId}/tier")]
  public async Task<SubscriptionRes> SetTier(string userId, [FromBody] SetTierReq req, CancellationToken ct)
  {
    await service.SetTier(userId, req.Tier, req.Days ?? 30, ct);
    return (await service.Effective(userId, ct)).ToRes(userId);
  }
}
