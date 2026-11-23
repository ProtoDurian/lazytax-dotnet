using Ctx.Proof.Contract.V1;
using Ctx.Proof.Domain;

namespace Ctx.Proof.Tests.Unit;

public sealed class ContractTests
{
  [Fact]
  public async Task Binding_needs_an_owned_habit_a_known_plugin_and_the_users_tier()
  {
    var habits = new FakeHabits();
    var plan = new FakePlan();
    var repo = new MemBindings();
    var strava = new MemStrava();
    var options = new ProofOptions { Strava = { Enabled = true } };
    var reg = new PluginRegistry([new ManualProofPlugin(), new StravaProofPlugin(strava)], options);
    var svc = new ProofService(reg, repo, habits, plan, new InlineUnit());
    var mine = habits.Add("u");
    var theirs = habits.Add("v");
    const string Settings = """{"sportTypes":["Run"]}""";

    // another user's habit: 404, nothing written
    (await FluentActions.Awaiting(() => svc.Bind("u", theirs, "strava", Settings, default)).Should().ThrowAsync<LazyTax.Platform.Problems.NotFound>()).Which.Status.Should().Be(404);
    // unknown plugin, always-bound plugin
    await FluentActions.Awaiting(() => svc.Bind("u", mine, "nope", Settings, default)).Should().ThrowAsync<LazyTax.Platform.Problems.NotFound>();
    await FluentActions.Awaiting(() => svc.Bind("u", mine, "manual", "{}", default)).Should().ThrowAsync<LazyTax.Platform.Problems.ValidationFailed>();
    // free tier cannot bind strava (pro)
    await FluentActions.Awaiting(() => svc.Bind("u", mine, "strava", Settings, default)).Should().ThrowAsync<Ctx.Plan.Contract.V1.TierInsufficient>();
    plan.Pro.Add("u");
    // pro but not connected
    await FluentActions.Awaiting(() => svc.Bind("u", mine, "strava", Settings, default)).Should().ThrowAsync<LazyTax.Platform.Problems.ValidationFailed>();
    repo.Rows.Should().BeEmpty();

    await strava.Upsert("u", 1, new StravaTokens("a", "r", DateTime.UtcNow.AddHours(1)), "activity:read_all", default);
    var bound = await svc.Bind("u", mine, "strava", Settings, default);
    bound.PluginKey.Should().Be("strava");
    // rebind replaces: still one row
    await svc.Bind("u", mine, "strava", """{"sportTypes":["Ride"]}""", default);
    repo.Rows.Should().ContainSingle().Which.Settings.Should().Contain("Ride");

    (await svc.Unbind("u", mine, default)).Should().BeTrue();
    (await svc.Unbind("u", mine, default)).Should().BeFalse();
    await FluentActions.Awaiting(() => svc.GetBinding(LazyTax.Platform.Data.Owner.User("u"), theirs, default)).Should().ThrowAsync<LazyTax.Platform.Problems.NotFound>();
  }
}
