using Ctx.Plan.Contract.V1;
using LazyTax.Platform.Guards;

namespace Ctx.Vacation.Domain;

/// <summary>Layer 5, step 1 (plan): a tier with no vacation windows stops here, before the lock.</summary>
public sealed class VacationQuotaGuard(IPlanV1 plan) : IGuard
{
  public GuardStage Stage => GuardStage.Plan;

  public string Label => "plan.quota(vacationWindows) > 0";

  public async Task Check(GuardContext ctx, CancellationToken ct)
  {
    var q = await plan.Quota(ctx.UserId, QuotaKind.VacationWindows, ct);
    if (q.Limit <= 0) throw new TierInsufficient(q.Tier, q.Key, q.Limit);
  }
}
