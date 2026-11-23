namespace Ctx.Plan.Domain;

public enum SubscriptionStatus
{
  PendingActivation,
  Active,
  Grace,
  Cancelled,
}

/// <summary>LazyTax's UserSubscriptionRecord (billing fields kept for the later billing service).</summary>
public sealed record SubscriptionRecord(
  string UserId,
  string Tier,
  SubscriptionStatus Status,
  DateTime PeriodStart,
  DateTime PeriodEnd,
  bool CancelAtPeriodEnd,
  string? NextTier);

public sealed record SubscriptionPrincipal(Guid Id, SubscriptionRecord Record, DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>One tier of the catalog, from config (LazyTax SubscriptionPlan).</summary>
public sealed record TierPlan(string Tier, long PriceCents, string Currency, IReadOnlyDictionary<string, int> Caps, int GracePeriodDays);
