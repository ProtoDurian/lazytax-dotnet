namespace Ctx.Plan.Domain;

/// <summary>Config <c>Contexts:plan</c>: the tier catalog (LazyTax SubscriptionOption).</summary>
public sealed class PlanOptions
{
  public int GracePeriodDays { get; set; } = 7;

  public Dictionary<string, TierOption> Tiers { get; set; } = [];
}

public sealed class TierOption
{
  public long PriceCents { get; set; }
  public string Currency { get; set; } = "USD";
  public int HabitsMax { get; set; }
  public int SkipsMonthly { get; set; }
  public int VacationWindowsYearly { get; set; }
  public int FreezeBase { get; set; }
}
