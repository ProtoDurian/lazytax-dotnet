using Ctx.Plan.Contract.V1;
using Ctx.Plan.Domain;

namespace Ctx.Plan.Api.V1;

/// <summary>One tier of the public catalog. HabitsMax is null when unlimited.</summary>
public sealed record PlanRes(string Key, long PriceCents, string Currency, int? HabitsMax, bool HabitsUnlimited, int SkipsMonthly, int VacationWindowsYearly, int FreezeBase);

public sealed record SubscriptionRes(string UserId, string Tier, string Status, string? PeriodStart, string? PeriodEnd, bool CancelAtPeriodEnd, string? NextTier);

/// <summary>Admin: set a user's tier (no billing in this model).</summary>
public sealed record SetTierReq(string Tier, int? Days);

public static class Mapper
{
  public static PlanRes ToRes(this TierPlan p)
  {
    var habits = p.Caps[EntitlementKeys.HabitsMax];
    return new PlanRes(p.Tier, p.PriceCents, p.Currency, habits == int.MaxValue ? null : habits, habits == int.MaxValue,
      p.Caps[EntitlementKeys.SkipsMonthly], p.Caps[EntitlementKeys.VacationWindowsYearly], p.Caps[EntitlementKeys.FreezeBase]);
  }

  /// <summary>No row (or one that grants nothing) presents as the free tier.</summary>
  public static SubscriptionRes ToRes(this SubscriptionPrincipal? p, string userId) => p is null
    ? new SubscriptionRes(userId, Tiers.Free, "none", null, null, false, null)
    : new SubscriptionRes(p.Record.UserId, p.Record.Tier, p.Record.Status.ToString().ToLowerInvariant(),
      p.Record.PeriodStart.ToString("O"), p.Record.PeriodEnd.ToString("O"), p.Record.CancelAtPeriodEnd, p.Record.NextTier);
}
