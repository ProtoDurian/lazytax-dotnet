using Ctx.Proof.Contract.V1;
using Ctx.Proof.Domain;
using Ctx.Proof.Gateways;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ctx.Proof.Tests.Unit;

public sealed class StravaServiceTests
{
  private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

  private readonly FixedClock clock = new(Now);
  private readonly FakeHabits habits = new();
  private readonly MemBindings bindings = new();
  private readonly MemStrava conns = new();
  private readonly RecordingOutbox outbox = new();
  private readonly FakeCheck check;
  private readonly FakeStravaApi api;
  private readonly StravaService svc;

  public StravaServiceTests()
  {
    this.check = new FakeCheck(this.habits);
    this.api = new FakeStravaApi(this.clock);
    var options = new ProofOptions { Strava = { Enabled = true } };
    var registry = new PluginRegistry([new ManualProofPlugin(), new StravaProofPlugin(this.conns)], options);
    var router = new ProofRouter(registry, this.bindings, this.check);
    this.svc = new StravaService(this.api, this.conns, new MemInbox(), this.bindings, router, this.outbox, new InlineUnit(), this.clock, NullLogger<StravaService>.Instance);
  }

  private StravaConnection Connect(string user, long athlete, DateTime? expires = null, string refresh = "r0")
  {
    var c = new StravaConnection(Guid.NewGuid(), user, athlete, new StravaTokens("a0", refresh, expires ?? Now.AddHours(1)), "activity:read_all", Now);
    this.conns.Rows[user] = c;
    return c;
  }

  private static StravaWebhookEvent Act(long athlete, long id, string aspect = "create") => new("activity", aspect, id, athlete, Now, false);

  [Fact]
  public async Task Connect_needs_the_activity_scope()
  {
    (await this.svc.Connect("u", "athlete-1", "read", default)).Should().Be(StravaConnectOutcome.ScopeMissing);
    this.conns.Rows.Should().BeEmpty();
    (await this.svc.Connect("u", "athlete-1", "read,activity:read_all", default)).Should().Be(StravaConnectOutcome.Connected);
    this.conns.Rows["u"].AthleteId.Should().Be(1);
  }

  [Fact]
  public async Task Connect_refuses_an_athlete_linked_to_another_user_and_revokes_the_new_grant()
  {
    this.Connect("first", 5);
    (await this.svc.Connect("second", "athlete-5", "activity:read_all", default)).Should().Be(StravaConnectOutcome.AthleteTaken);
    this.conns.Rows.Should().ContainKey("first").And.NotContainKey("second");
    this.api.RevokedTokens.Should().ContainSingle();
  }

  [Fact]
  public async Task An_activity_event_routes_proof_for_the_athletes_user()
  {
    this.Connect("u", 7);
    var h = this.habits.Add("u");
    await this.bindings.Upsert("u", h, ProofPlugins.Strava, 1, """{"sportTypes":["Run"]}""", default);
    this.api.SeedActivity(99, new StravaActivity { SportType = "Run", StartDate = Now });
    await this.svc.Process(Act(7, 99), default);
    var report = this.check.Reports.Should().ContainSingle().Subject;
    (report.UserId, report.HabitId, report.Evidence.ProofKey, report.Evidence.ExternalId).Should().Be(("u", h, "strava", "99"));
  }

  [Fact]
  public async Task An_event_for_an_unknown_athlete_does_nothing()
  {
    await this.svc.Process(Act(404, 1), default);
    this.api.ActivityCalls.Should().Be(0);
    this.check.Reports.Should().BeEmpty();
  }

  [Fact]
  public async Task A_deleted_activity_revokes_only_that_users_evidence()
  {
    this.Connect("u", 7);
    await this.svc.Process(Act(7, 99, "delete"), default);
    var e = this.outbox.Events.Should().ContainSingle().Subject;
    e.Name.Should().Be(ProofEvents.EvidenceRevoked);
    e.Payload.Should().Be(new EvidenceRevoked("u", "strava", "99"));
  }

  [Fact]
  public async Task Deauthorize_purges_the_connection_and_strava_bindings_but_not_other_proofs()
  {
    this.Connect("u", 7);
    var h1 = this.habits.Add("u");
    var h2 = this.habits.Add("u");
    await this.bindings.Upsert("u", h1, ProofPlugins.Strava, 1, """{"sportTypes":["Run"]}""", default);
    await this.bindings.Upsert("u", h2, "other", 1, "{}", default);
    await this.svc.Process(new StravaWebhookEvent("athlete", "update", 7, 7, Now, true), default);
    this.conns.Rows.Should().BeEmpty();
    this.bindings.Rows.Should().ContainSingle().Which.PluginKey.Should().Be("other");
    this.outbox.Events.Should().ContainSingle().Which.Payload.Should().Be(new EvidenceRevoked("u", "strava", null));
  }

  [Fact]
  public async Task An_athlete_update_that_is_not_a_deauthorize_is_ignored()
  {
    this.Connect("u", 7);
    await this.svc.Process(new StravaWebhookEvent("athlete", "update", 7, 7, Now, false), default);
    this.conns.Rows.Should().ContainKey("u");
  }

  [Fact]
  public async Task Disconnect_revokes_at_strava_then_purges_and_reports_missing()
  {
    this.Connect("u", 7, refresh: "r-live");
    (await this.svc.Disconnect(LazyTax.Platform.Data.Owner.User("u"), default)).Should().BeTrue();
    this.api.RevokedTokens.Should().Equal("r-live");
    this.conns.Rows.Should().BeEmpty();
    (await this.svc.Disconnect(LazyTax.Platform.Data.Owner.User("u"), default)).Should().BeFalse();
  }

  [Fact]
  public async Task An_expiring_token_is_refreshed_once_under_the_lease_and_saved()
  {
    this.Connect("u", 7, expires: Now.AddMinutes(1));
    this.api.SeedActivity(1, new StravaActivity { SportType = "Run", StartDate = Now });
    await this.svc.Process(Act(7, 1), default);
    this.api.RefreshCalls.Should().Be(1);
    this.conns.Rows["u"].Tokens.AccessToken.Should().StartWith("access-");
    this.conns.LeaseHeld.Should().BeEmpty("the lease is released after the save");
    await this.svc.Process(Act(7, 1), default);
    this.api.RefreshCalls.Should().Be(1, "a fresh token is not refreshed again");
  }

  [Fact]
  public async Task A_refresh_in_progress_elsewhere_fails_the_event_so_the_inbox_retries_it()
  {
    this.Connect("u", 7, expires: Now.AddMinutes(1));
    this.conns.LeaseHeld.Add("u");
    var act = () => this.svc.Process(Act(7, 1), default);
    await act.Should().ThrowAsync<InvalidOperationException>();
    this.api.RefreshCalls.Should().Be(0);
  }

  [Fact]
  public async Task A_rejected_refresh_token_disconnects_the_user()
  {
    this.Connect("u", 7, expires: Now.AddMinutes(1), refresh: "dead");
    this.api.RejectRefreshToken("dead");
    await this.svc.Process(Act(7, 1), default);
    this.conns.Rows.Should().BeEmpty();
    this.outbox.Events.Should().ContainSingle().Which.Name.Should().Be(ProofEvents.EvidenceRevoked);
    this.api.ActivityCalls.Should().Be(0);
  }

  [Fact]
  public async Task Strava_http_never_runs_inside_a_unit_of_work()
  {
    // The activity fetch and the refresh happen before uow.Start; only the routing is inside.
    var unit = new InlineUnit();
    this.Connect("u", 7);
    this.api.SeedActivity(1, new StravaActivity { SportType = "Run", StartDate = Now });
    var options = new ProofOptions { Strava = { Enabled = true } };
    var router = new ProofRouter(new PluginRegistry([new StravaProofPlugin(this.conns)], options), this.bindings, this.check);
    var probe = new ProbeApi(this.api, () => unit.Active);
    var s = new StravaService(probe, this.conns, new MemInbox(), this.bindings, router, this.outbox, new ActiveUnit(unit), this.clock, NullLogger<StravaService>.Instance);
    await s.Process(Act(7, 1), default);
    probe.CalledInsideUnit.Should().BeFalse();
  }

  private sealed class ActiveUnit(InlineUnit inner) : LazyTax.Platform.Data.IUnitOfWork
  {
    public async Task<T> Start<T>(Func<Task<T>> work, CancellationToken ct = default)
    {
      inner.Active = true;
      try { return await work(); } finally { inner.Active = false; }
    }

    public Task Start(Func<Task> work, CancellationToken ct = default) => this.Start<int>(async () => { await work(); return 0; });

    public Task LockUser(string userId, CancellationToken ct = default) => Task.CompletedTask;
  }

  private sealed class ProbeApi(IStravaApi inner, Func<bool> inUnit) : IStravaApi
  {
    public bool CalledInsideUnit { get; private set; }

    private void Mark()
    {
      if (inUnit()) this.CalledInsideUnit = true;
    }

    public Task<StravaGrant?> ExchangeCode(string code, CancellationToken ct) { this.Mark(); return inner.ExchangeCode(code, ct); }
    public Task<StravaTokens?> Refresh(string refreshToken, CancellationToken ct) { this.Mark(); return inner.Refresh(refreshToken, ct); }
    public Task<StravaActivity?> GetActivity(string accessToken, long activityId, CancellationToken ct) { this.Mark(); return inner.GetActivity(accessToken, activityId, ct); }
    public Task Revoke(string token, CancellationToken ct) { this.Mark(); return inner.Revoke(token, ct); }
  }
}
