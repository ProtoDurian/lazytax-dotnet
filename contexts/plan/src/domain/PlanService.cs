using Ctx.Account.Contract.V1;
using Ctx.Plan.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;

namespace Ctx.Plan.Domain;

/// <summary>Plan rules: effective tier, quotas, freeze cap, and the admin tier change (no billing here).</summary>
public sealed class PlanService(ISubscriptionRepository repo, Catalog catalog, IAccountV1 account, IUnitOfWork uow, TimeProvider clock)
{
  public async Task<SubscriptionPrincipal?> Effective(string userId, CancellationToken ct) =>
    catalog.Effective(await repo.GetByUser(userId, ct), clock.GetUtcNow().UtcDateTime);

  public async Task<string> EffectiveTier(string userId, CancellationToken ct) =>
    (await this.Effective(userId, ct))?.Record.Tier ?? Tiers.Free;

  public async Task<Quota> Quota(string userId, QuotaKind kind, CancellationToken ct)
  {
    var tier = await this.EffectiveTier(userId, ct);
    var key = EntitlementKeys.For(kind);
    return new Quota(tier, key, catalog.Limit(tier, key));
  }

  public async Task<int> FreezeCap(string userId, int maxStreak, CancellationToken ct)
  {
    var tier = await this.EffectiveTier(userId, ct);
    return Catalog.FreezeMax(catalog.Limit(tier, EntitlementKeys.FreezeBase), maxStreak);
  }

  /// <summary>Tiers have no explicit order: a plan is "at least" another when it costs as much or more.</summary>
  public async Task EnsureTier(string userId, string minTier, string feature, CancellationToken ct)
  {
    var tier = await this.EffectiveTier(userId, ct);
    if (catalog.Get(tier).PriceCents >= catalog.Get(minTier).PriceCents) return;
    throw new TierInsufficient(tier, feature, 0, $"{feature} needs the {minTier} plan or higher");
  }

  /// <summary>Admin: set the user's tier for <paramref name="days"/> days from now (Active). "free" cancels.</summary>
  public Task<SubscriptionPrincipal> SetTier(string userId, string tier, int days, CancellationToken ct)
  {
    catalog.Get(tier);
    if (days is < 1 or > 3660) throw new ValidationFailed("Invalid SetTierReq", "Days", "Days must be 1 to 3660");
    return uow.Start(async () =>
    {
      if (!await account.Exists(userId, ct)) throw new NotFound("User", userId);
      var now = clock.GetUtcNow().UtcDateTime;
      var status = tier == Tiers.Free ? SubscriptionStatus.Cancelled : SubscriptionStatus.Active;
      return await repo.Upsert(new SubscriptionRecord(userId, tier, status, now, now.AddDays(days), false, null), ct);
    }, ct);
  }

  /// <summary>LazyTax Cancel: keep the paid tier until the period ends, then fall back to free.</summary>
  public Task<SubscriptionPrincipal?> Cancel(string userId, CancellationToken ct) =>
    uow.Start(async () =>
    {
      var current = await this.Effective(userId, ct);
      if (current is null) return null;
      return await repo.Upsert(current.Record with { CancelAtPeriodEnd = true, NextTier = null }, ct);
    }, ct);
}
