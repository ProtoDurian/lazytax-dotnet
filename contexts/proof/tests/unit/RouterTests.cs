using Ctx.Check.Contract.V1;
using Ctx.Proof.Contract.V1;
using Ctx.Proof.Domain;
using LazyTax.Platform.Problems;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ctx.Proof.Tests.Unit;

public sealed class RouterTests
{
  private const string User = "u_router";
  private static readonly DateTime At = new(2026, 11, 20, 8, 30, 0, DateTimeKind.Utc);

  private readonly FakeHabits habits = new();
  private readonly FakeCheck check;
  private readonly MemBindings bindings = new();
  private readonly MemStrava strava = new();
  private readonly ProofRouter router;

  public RouterTests()
  {
    this.check = new FakeCheck(this.habits);
    var options = new ProofOptions { Strava = { Enabled = true } };
    var registry = new PluginRegistry(
      [new ManualProofPlugin(), new NfcProofPlugin(new NfcTagLookup(new NoTags())), new StravaProofPlugin(this.strava)], options);
    this.router = new ProofRouter(registry, this.bindings, this.check);
  }

  private static ProofEvent Manual(string user, Guid? habit, string ext = "e1") =>
    new(ProofPlugins.Manual, user, ext, At, habit);

  [Fact]
  public async Task Manual_proves_an_owned_habit_with_no_binding()
  {
    var h = this.habits.Add(User);
    var outcomes = await this.router.Route(Manual(User, h), default);
    outcomes.Should().ContainSingle().Which.ExecutionId.Should().NotBeNull();
    this.check.Reports.Should().ContainSingle().Which.Evidence.Verdict.Should().Be(EvidenceVerdict.Passed);
  }

  [Fact]
  public async Task Manual_on_another_users_habit_is_404_and_reports_nothing()
  {
    var h = this.habits.Add("someone_else");
    var act = () => this.router.Route(Manual(User, h), default);
    (await act.Should().ThrowAsync<Problem>()).Which.Status.Should().Be(404);
    this.check.Reports.Should().BeEmpty();
  }

  [Fact]
  public async Task A_replay_changes_nothing()
  {
    var h = this.habits.Add(User);
    await this.router.Route(Manual(User, h), default);
    var second = await this.router.Route(Manual(User, h), default);
    second.Should().ContainSingle().Which.ExecutionId.Should().BeNull();
    this.check.Reports.Should().HaveCount(1);
  }

  [Fact]
  public async Task Nothing_pending_means_no_verdict_and_no_report()
  {
    var h = this.habits.Add(User);
    this.check.NothingPending = true;
    var outcomes = await this.router.Route(Manual(User, h), default);
    outcomes.Should().ContainSingle().Which.ExecutionId.Should().BeNull();
    this.check.Reports.Should().BeEmpty();
  }

  [Fact]
  public async Task Unknown_plugin_is_404()
  {
    var act = () => this.router.Route(new ProofEvent("nope", User, "e", At), default);
    (await act.Should().ThrowAsync<Problem>()).Which.Status.Should().Be(404);
  }

  private async Task<Guid> BoundStrava(string user, params string[] sports)
  {
    var h = this.habits.Add(user);
    await this.bindings.Upsert(user, h, ProofPlugins.Strava, 1, $"{{\"sportTypes\":[{string.Join(',', sports.Select(s => $"\"{s}\""))}]}}", default);
    return h;
  }

  private static ProofEvent Activity(string user, string sport, bool manual = false, string ext = "42") =>
    new(ProofPlugins.Strava, user, ext, At, null, $"{{\"sportType\":\"{sport}\",\"startDate\":\"2026-11-20T08:30:00Z\",\"manual\":{manual.ToString().ToLowerInvariant()}}}");

  [Fact]
  public async Task Strava_activity_of_a_bound_sport_proves_every_bound_habit()
  {
    var a = await this.BoundStrava(User, "Run");
    var b = await this.BoundStrava(User, "Run", "Ride");
    var outcomes = await this.router.Route(Activity(User, "Run"), default);
    outcomes.Select(o => o.HabitId).Should().BeEquivalentTo([a, b]);
    outcomes.Should().OnlyContain(o => o.ExecutionId != null);
  }

  [Fact]
  public async Task Strava_wrong_sport_or_hand_typed_activity_is_not_proof()
  {
    await this.BoundStrava(User, "Run");
    (await this.router.Route(Activity(User, "Swim"), default)).Should().OnlyContain(o => o.ExecutionId == null);
    (await this.router.Route(Activity(User, "Run", manual: true, ext: "43"), default)).Should().OnlyContain(o => o.ExecutionId == null);
    this.check.Reports.Should().BeEmpty();
  }

  [Fact]
  public async Task Strava_never_proves_another_users_habit()
  {
    await this.BoundStrava("other_user", "Run");
    (await this.router.Route(Activity(User, "Run"), default)).Should().BeEmpty();
    this.check.Reports.Should().BeEmpty();
  }

  [Fact]
  public async Task Untargeted_event_skips_a_habit_check_refuses_but_still_proves_the_rest()
  {
    var off = await this.BoundStrava(User, "Run");
    var on = await this.BoundStrava(User, "Run");
    this.check.Disabled.Add(off);
    var outcomes = await this.router.Route(Activity(User, "Run"), default);
    outcomes.Single(o => o.HabitId == off).ExecutionId.Should().BeNull();
    outcomes.Single(o => o.HabitId == on).ExecutionId.Should().NotBeNull();
  }

  [Fact]
  public async Task Targeted_non_always_bound_plugin_needs_that_binding()
  {
    var h = this.habits.Add(User); // owned, but bound to nothing
    var e = Activity(User, "Run") with { HabitId = h };
    (await this.router.Route(e, default)).Should().BeEmpty();
  }

  private sealed class NoTags : INfcTagRepository
  {
    public Task<NfcTag?> Get(LazyTax.Platform.Data.Owner owner, string tagId, CancellationToken ct) => Task.FromResult<NfcTag?>(null);
    public Task<NfcTag?> Claim(string userId, string tagId, Guid habitId, CancellationToken ct) => throw new NotImplementedException();
    public Task<bool> Delete(LazyTax.Platform.Data.Owner owner, string tagId, CancellationToken ct) => throw new NotImplementedException();
    public Task<IReadOnlyList<Guid>> ListTaggedHabits(LazyTax.Platform.Data.Owner owner, CancellationToken ct) => throw new NotImplementedException();
    public Task DeleteForHabits(string userId, IReadOnlyCollection<Guid> habitIds, CancellationToken ct) => throw new NotImplementedException();
  }
}
