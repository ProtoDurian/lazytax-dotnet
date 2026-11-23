using Ctx.Plan.Contract.V1;
using LazyTax.Platform.Guards;

namespace Ctx.Check.Domain;

/// <summary>Layer 5, step 1: a plan with no skips stops here. The count against the month runs later, under the user lock.</summary>
public sealed class SkipQuotaGuard(IPlanV1 plan) : IGuard
{
  public GuardStage Stage => GuardStage.Plan;

  public string Label => "plan.quota(skips)";

  public async Task Check(GuardContext ctx, CancellationToken ct)
  {
    var q = await plan.Quota(ctx.UserId, QuotaKind.Skips, ct);
    if (q.Limit <= 0) throw new TierInsufficient(q.Tier, q.Key, q.Limit);
  }
}
