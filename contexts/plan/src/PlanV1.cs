using Ctx.Plan.Contract.V1;
using Ctx.Plan.Domain;

namespace Ctx.Plan;

/// <summary>plan.v1 for other contexts.</summary>
public sealed class PlanV1(PlanService service) : IPlanV1
{
  public Task<string> EffectiveTier(string userId, CancellationToken ct = default) => service.EffectiveTier(userId, ct);

  public Task<Quota> Quota(string userId, QuotaKind kind, CancellationToken ct = default) => service.Quota(userId, kind, ct);

  public Task<int> FreezeCap(string userId, int maxStreak, CancellationToken ct = default) => service.FreezeCap(userId, maxStreak, ct);

  public Task EnsureTier(string userId, string minTier, string feature, CancellationToken ct = default) => service.EnsureTier(userId, minTier, feature, ct);
}
