using Ctx.Plan.Contract.V1;
using LazyTax.Platform.Guards;

namespace Ctx.Habit.Domain;

/// <summary>Layer 5, plan stage: a tier that allows no habits is refused before anything else runs.</summary>
public sealed class HabitQuotaGuard(IPlanV1 plan) : IGuard
{
  public GuardStage Stage => GuardStage.Plan;
  public string Label => "plan.quota(habits)";

  public async Task Check(GuardContext ctx, CancellationToken ct)
  {
    var q = await plan.Quota(ctx.UserId, QuotaKind.Habits, ct);
    if (q.Limit <= 0) throw new TierInsufficient(q.Tier, q.Key, q.Limit);
  }
}
