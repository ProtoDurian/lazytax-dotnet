using Ctx.Plan.Domain;

namespace Ctx.Plan.Tests.Unit;

public sealed class CatalogTests
{
  private static readonly Catalog Plans = new(new PlanOptions
  {
    GracePeriodDays = 7,
    Tiers = new()
    {
      ["free"] = new TierOption { PriceCents = 0, VacationWindowsYearly = 0, FreezeBase = 0 },
      ["pro"] = new TierOption { PriceCents = 499, VacationWindowsYearly = 6, FreezeBase = 14 },
    },
  });

  private static SubscriptionPrincipal Sub(SubscriptionStatus status, DateTime end, bool cancel = false) =>
    new(Guid.NewGuid(), new SubscriptionRecord("u", "pro", status, end.AddDays(-30), end, cancel, null), end, end);

  private static readonly DateTime Now = new(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);

  [Fact]
  public void Active_in_period_is_effective() => Plans.Effective(Sub(SubscriptionStatus.Active, Now.AddDays(3)), Now).Should().NotBeNull();

  [Fact]
  public void Past_period_within_grace_is_effective() => Plans.Effective(Sub(SubscriptionStatus.Grace, Now.AddDays(-3)), Now).Should().NotBeNull();

  [Fact]
  public void Past_grace_is_free() => Plans.Effective(Sub(SubscriptionStatus.Active, Now.AddDays(-8)), Now).Should().BeNull();

  [Fact]
  public void Cancelled_at_period_end_stops_at_the_end() =>
    Plans.Effective(Sub(SubscriptionStatus.Active, Now.AddDays(-1), cancel: true), Now).Should().BeNull();

  [Fact]
  public void Pending_activation_grants_nothing() => Plans.Effective(Sub(SubscriptionStatus.PendingActivation, Now.AddDays(3)), Now).Should().BeNull();

  [Theory]
  [InlineData(0, 10, 0)]
  [InlineData(14, 0, 14)]
  [InlineData(14, 30, 15)]
  [InlineData(14, 80, 16)]
  [InlineData(14, 150, 17)]
  [InlineData(14, 300, 18)]
  public void Freeze_max_adds_streak_bonuses(int baseCap, int streak, int expected) => Catalog.FreezeMax(baseCap, streak).Should().Be(expected);

  [Fact]
  public void Unknown_tier_is_400() => FluentActions.Invoking(() => Plans.Get("gold")).Should().Throw<InvalidSubscriptionTier>();
}
