using Ctx.Protection.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Protection.Tests.Integration;

public sealed class ProtectionContractTests(KernelApp root)
{
  private readonly ProtectionApp app = new(root);

  private Task<bool> Consume(string u, DateOnly d) =>
    this.app.InScope(sp => sp.GetRequiredService<IProtectionV1>().TryConsumeFreeze(u, d));

  [Fact]
  public async Task Consume_without_freezes_is_false()
  {
    var u = TestTokens.NewUser();
    (await this.Consume(u, new DateOnly(2026, 11, 20))).Should().BeFalse();
    (await this.app.Count("FreezeConsumptions", u)).Should().Be(0);
  }

  [Fact]
  public async Task Consume_spends_one_per_day_and_is_idempotent_for_the_same_day()
  {
    var u = TestTokens.NewUser();
    await this.app.Seed(u, 2);
    var day = new DateOnly(2026, 11, 20);
    (await this.Consume(u, day)).Should().BeTrue();
    (await this.Consume(u, day)).Should().BeTrue();
    (await this.app.Count("FreezeConsumptions", u)).Should().Be(1);
    (await this.app.BalanceOf(u)).Balance.Should().Be(1);
    (await this.Consume(u, day.AddDays(1))).Should().BeTrue();
    (await this.Consume(u, day.AddDays(2))).Should().BeFalse();
    (await this.app.BalanceOf(u)).Balance.Should().Be(0);
  }

  [Fact]
  public async Task Parallel_consumes_never_spend_more_than_the_balance()
  {
    var u = TestTokens.NewUser();
    await this.app.Seed(u, 1);
    var day = new DateOnly(2026, 11, 20);
    var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => this.Consume(u, day.AddDays(i % 4))));
    results.Count(r => r).Should().BeGreaterThan(0);
    (await this.app.Count("FreezeConsumptions", u)).Should().Be(1); // exactly one day got the single freeze
    (await this.app.BalanceOf(u)).Balance.Should().Be(0);
  }

  [Fact]
  public async Task A_consume_rolls_back_with_its_unit_of_work()
  {
    var u = TestTokens.NewUser();
    await this.app.Seed(u, 1);
    var act = () => this.app.Unit(async sp =>
    {
      (await sp.GetRequiredService<IProtectionV1>().TryConsumeFreeze(u, new DateOnly(2026, 11, 20))).Should().BeTrue();
      throw new InvalidOperationException("the settle failed after the freeze");
    });
    await act.Should().ThrowAsync<InvalidOperationException>();
    (await this.app.Count("FreezeConsumptions", u)).Should().Be(0);
    (await this.app.BalanceOf(u)).Balance.Should().Be(1);
  }

  [Fact]
  public async Task Reconcile_cuts_banked_freezes_to_the_cap_for_good()
  {
    var u = TestTokens.NewUser();
    await this.app.Seed(u, 6);
    this.app.Cap = 2;
    var b = await this.app.InScope(sp => sp.GetRequiredService<IProtectionV1>().ReconcileCap(u, 12));
    b.Should().Be(new FreezeBalance(u, 2, 2));
    this.app.Cap = 10; // a later upgrade does not restore them
    (await this.app.BalanceOf(u)).Balance.Should().Be(2);
    this.app.Cap = 3;
  }

  [Fact]
  public async Task Balance_refuses_owner_anyone()
  {
    var act = () => this.app.InScope(sp => sp.GetRequiredService<IProtectionV1>().Balance(Owner.Anyone()));
    await act.Should().ThrowAsync<ArgumentException>();
  }

  [Fact]
  public async Task Settled_events_are_recorded_once_and_the_max_streak_is_remembered()
  {
    var u = TestTokens.NewUser();
    var h = Guid.NewGuid();
    await this.app.Settled(u, h, "2026-11-16", "missed", maxStreak: 4);
    await this.app.Settled(u, h, "2026-11-16", "done", maxStreak: 31);
    (await this.app.Count("SettledDays", u)).Should().Be(1);
    (await this.app.Root.Count("SELECT \"MaxStreak\" FROM protection.\"UserProtections\" WHERE \"UserId\" = @u", ("u", u))).Should().Be(31);
  }

  [Fact]
  public async Task Weekly_award_clamps_to_the_cap()
  {
    var u = TestTokens.NewUser();
    await this.app.Seed(u, this.app.Cap); // already full
    await this.app.Settled(u, Guid.NewGuid(), "2026-11-16", "done");
    var n = await this.app.InScope(sp => sp.GetRequiredService<Ctx.Protection.Domain.ProtectionService>().AwardWeekly(u, null, default));
    n.Should().Be(1);
    (await this.app.BalanceOf(u)).Balance.Should().Be(this.app.Cap);
  }
}
