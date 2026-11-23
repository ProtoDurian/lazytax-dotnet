using Ctx.Plan.Contract.V1;
using LazyTax.Platform.Problems;

namespace Ctx.Plan.Domain;

/// <summary>400: not a tier of the catalog (LazyTax InvalidSubscriptionTier).</summary>
public sealed class InvalidSubscriptionTier(string tier)
  : Problem(400, "invalid_subscription_tier", "Invalid Subscription Tier", $"'{tier}' is not a subscribable tier", new { tier });

/// <summary>The plan catalog over config (LazyTax SubscriptionPlanProvider), plus the pure rules.</summary>
public sealed class Catalog(PlanOptions options)
{
  public IReadOnlyList<TierPlan> All() => [.. options.Tiers.Keys.Select(this.Get).OrderBy(p => p.PriceCents)];

  public bool Has(string tier) => options.Tiers.ContainsKey(tier);

  public TierPlan Get(string tier)
  {
    if (!options.Tiers.TryGetValue(tier, out var t)) throw new InvalidSubscriptionTier(tier);
    return new TierPlan(tier, t.PriceCents, t.Currency, new Dictionary<string, int>
    {
      [EntitlementKeys.HabitsMax] = t.HabitsMax,
      [EntitlementKeys.SkipsMonthly] = t.SkipsMonthly,
      [EntitlementKeys.VacationWindowsYearly] = t.VacationWindowsYearly,
      [EntitlementKeys.FreezeBase] = t.FreezeBase,
    }, options.GracePeriodDays);
  }

  /// <summary>Unknown keys stay permissive (LazyTax GetLimitForTier).</summary>
  public int Limit(string tier, string key) => this.Get(tier).Caps.TryGetValue(key, out var cap) ? cap : int.MaxValue;

  /// <summary>
  /// LazyTax EffectiveSubscription.Resolve: the stored row grants its tier only while Active or in Grace, and
  /// until PeriodEnd (+ grace unless cancelled at period end). Null means free.
  /// </summary>
  public SubscriptionPrincipal? Effective(SubscriptionPrincipal? sub, DateTime nowUtc)
  {
    if (sub?.Record.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Grace)) return null;
    var rec = sub.Record;
    if (rec.Tier == Tiers.Free || !this.Has(rec.Tier)) return null;
    if (nowUtc > rec.PeriodEnd)
    {
      if (rec.CancelAtPeriodEnd) return null;
      var next = rec.NextTier ?? rec.Tier;
      if (!this.Has(next) || nowUtc > rec.PeriodEnd.AddDays(this.Get(next).GracePeriodDays)) return null;
    }
    return sub;
  }

  /// <summary>LazyTax FreezePolicy.ComputeFreezeMax.</summary>
  public static int FreezeMax(int baseCap, int maxStreak)
  {
    if (baseCap <= 0) return 0;
    var bonus = 0;
    if (maxStreak >= 30) bonus += 1;
    if (maxStreak >= 80) bonus += 1;
    if (maxStreak >= 150) bonus += 1 + ((maxStreak - 150) / 150);
    return baseCap + bonus;
  }
}
