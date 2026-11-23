using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ctx.Proof.Domain;
using Ctx.Proof.Gateways;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Proof.Tests.Integration;

public sealed class StravaApiTests(KernelApp app)
{
  private readonly ProofHost host = ProofHost.On(app);
  private static long NextAthlete = 7_000_000 + Random.Shared.Next(1_000_000);

  private async Task<long> Connect(string user)
  {
    var athlete = Interlocked.Increment(ref NextAthlete);
    var url = (await (await this.host.As(user).GetAsync($"/api/v1/Strava/connect-url/{user}")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
    var state = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["state"]!;
    var res = await this.host.NoRedirect().GetAsync($"/api/v1/Strava/callback?code=athlete-{athlete}&scope=read,activity:read_all&state={Uri.EscapeDataString(state)}");
    res.StatusCode.Should().Be(HttpStatusCode.Redirect);
    res.Headers.Location!.ToString().Should().Contain("result=connected");
    return athlete;
  }

  private async Task<Guid> BindStrava(string user, params string[] sports)
  {
    this.host.Plan.Pro.Add(user);
    var h = this.host.Habits.Add(user);
    var res = await this.host.As(user).PutAsJsonAsync($"/api/v1/Proof/{user}/{h}", new { pluginKey = "strava", settings = JsonSerializer.Serialize(new { sportTypes = sports }) });
    res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
    return h;
  }

  private static HttpRequestMessage Webhook(object body, DateTime? at = null, string secret = "dev-webhook-secret-change-me", bool sign = true)
  {
    var raw = JsonSerializer.Serialize(body);
    var ts = new DateTimeOffset(at ?? DateTime.UtcNow).ToUnixTimeSeconds().ToString();
    var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Strava/webhook") { Content = new StringContent(raw, Encoding.UTF8, "application/json") };
    if (sign)
    {
      req.Headers.Add("X-Lazytax-Timestamp", ts);
      req.Headers.Add("X-Lazytax-Signature", WebhookSignature.Sign(secret, ts, raw));
    }

    return req;
  }

  private static object Event(string type, string aspect, long id, long owner, object? updates = null) =>
    new { object_type = type, aspect_type = aspect, object_id = id, owner_id = owner, event_time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), updates };

  private Task Drain() => this.host.InScope(sp => sp.GetRequiredService<StravaService>().DrainInbox(50, TimeSpan.FromMinutes(5), 5, default));

  private FakeStravaApi Fake => this.host.Factory.Services.GetRequiredService<FakeStravaApi>();

  [Fact]
  public async Task Connect_status_and_disconnect()
  {
    var u = TestTokens.NewUser();
    var c = this.host.As(u);
    (await c.GetFromJsonAsync<JsonElement>($"/api/v1/Strava/{u}")).GetProperty("connected").GetBoolean().Should().BeFalse();
    await this.Connect(u);
    (await c.GetFromJsonAsync<JsonElement>($"/api/v1/Strava/{u}")).GetProperty("connected").GetBoolean().Should().BeTrue();
    (await app.Count("SELECT count(*) FROM proof.\"StravaConnections\" WHERE \"UserId\" = @u AND \"AccessToken\" LIKE 'access-%'", ("u", u))).Should().Be(0, "tokens are stored encrypted");
    (await c.DeleteAsync($"/api/v1/Strava/{u}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await c.DeleteAsync($"/api/v1/Strava/{u}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    this.Fake.RevokedTokens.Should().NotBeEmpty();
  }

  [Fact]
  public async Task Callback_outcomes_always_end_at_the_app()
  {
    var u = TestTokens.NewUser();
    var url = (await (await this.host.As(u).GetAsync($"/api/v1/Strava/connect-url/{u}")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
    var state = Uri.EscapeDataString(System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["state"]!);
    var anon = this.host.NoRedirect();
    async Task<string> Result(string q) => (await anon.GetAsync("/api/v1/Strava/callback" + q)).Headers.Location!.ToString();
    (await Result("?code=x&state=forged")).Should().Contain("result=expired");
    (await Result($"?error=access_denied&state={state}")).Should().Contain("result=denied");
    (await Result($"?code=athlete-1&scope=read&state={state}")).Should().Contain("result=scope_missing");
    (await Result($"?code=bad-code&scope=activity:read_all&state={state}")).Should().Contain("result=error");
  }

  [Fact]
  public async Task An_athlete_cannot_be_linked_to_two_users()
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    var athlete = await this.Connect(a);
    var url = (await (await this.host.As(b).GetAsync($"/api/v1/Strava/connect-url/{b}")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
    var state = Uri.EscapeDataString(System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["state"]!);
    var res = await this.host.NoRedirect().GetAsync($"/api/v1/Strava/callback?code=athlete-{athlete}&scope=activity:read_all&state={state}");
    res.Headers.Location!.ToString().Should().Contain("result=athlete_taken");
    (await app.Count("SELECT count(*) FROM proof.\"StravaConnections\" WHERE \"AthleteId\" = @a", ("a", athlete))).Should().Be(1);
  }

  [Fact]
  public async Task Another_users_connection_is_403_by_path_and_never_visible()
  {
    var a = TestTokens.NewUser();
    await this.Connect(a);
    var b = TestTokens.NewUser();
    (await this.host.As(b).GetAsync($"/api/v1/Strava/{a}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await this.host.As(b).DeleteAsync($"/api/v1/Strava/{a}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await this.host.Anonymous().GetAsync($"/api/v1/Strava/{a}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await this.host.As(b).GetFromJsonAsync<JsonElement>($"/api/v1/Strava/{b}")).GetProperty("connected").GetBoolean().Should().BeFalse();
    (await this.host.As(b).DeleteAsync($"/api/v1/Strava/{b}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await app.Count("SELECT count(*) FROM proof.\"StravaConnections\" WHERE \"UserId\" = @u", ("u", a))).Should().Be(1);
  }

  [Fact]
  public async Task A_signed_activity_webhook_completes_the_bound_habit_through_the_inbox()
  {
    var u = TestTokens.NewUser();
    var athlete = await this.Connect(u);
    var h = await this.BindStrava(u, "Run");
    const long Activity = 5551;
    this.Fake.SeedActivity(Activity, new StravaActivity { SportType = "Run", StartDate = DateTime.UtcNow, Manual = false });

    var res = await this.host.Anonymous().SendAsync(Webhook(Event("activity", "create", Activity, athlete)));
    res.StatusCode.Should().Be(HttpStatusCode.OK);
    (await app.Count("SELECT count(*) FROM proof.\"StravaEvents\" WHERE \"OwnerId\" = @a", ("a", athlete))).Should().Be(1);
    await this.Drain();
    (await app.Count("SELECT count(*) FROM proof.\"StravaEvents\" WHERE \"OwnerId\" = @a", ("a", athlete))).Should().Be(0);
    this.host.Check.Reports.Should().ContainSingle(r => r.UserId == u && r.HabitId == h && r.Evidence.ProofKey == "strava" && r.Evidence.ExternalId == "5551");

    // a replay of the same activity changes nothing
    await this.host.Anonymous().SendAsync(Webhook(Event("activity", "update", Activity, athlete)));
    await this.Drain();
    this.host.Check.Reports.Count(r => r.HabitId == h).Should().Be(1);
  }

  [Fact]
  public async Task Webhook_needs_a_fresh_valid_signature()
  {
    var anon = this.host.Anonymous();
    var body = Event("activity", "create", 1, 2);
    (await anon.SendAsync(Webhook(body, sign: false))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await anon.SendAsync(Webhook(body, secret: "wrong"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await anon.SendAsync(Webhook(body, at: DateTime.UtcNow.AddMinutes(-10)))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await anon.SendAsync(Webhook(body, at: DateTime.UtcNow.AddMinutes(10)))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await app.Count("SELECT count(*) FROM proof.\"StravaEvents\" WHERE \"OwnerId\" = 2 AND \"ObjectId\" = 1")).Should().Be(0);
  }

  [Fact]
  public async Task A_signed_but_unreadable_webhook_body_is_answered_200_and_dropped()
  {
    var before = await app.Count("SELECT count(*) FROM proof.\"StravaEvents\"");
    (await this.host.Anonymous().SendAsync(Webhook(new { hello = "world" }))).StatusCode.Should().Be(HttpStatusCode.OK);
    (await app.Count("SELECT count(*) FROM proof.\"StravaEvents\"")).Should().BeGreaterThanOrEqualTo(before);
    (await app.Count("SELECT count(*) FROM proof.\"StravaEvents\" WHERE \"ObjectType\" NOT IN ('activity','athlete')")).Should().Be(0);
  }

  [Fact]
  public async Task Webhook_challenge_echoes_only_for_the_verify_token()
  {
    var anon = this.host.Anonymous();
    var ok = await anon.GetAsync("/api/v1/Strava/webhook?hub.mode=subscribe&hub.verify_token=dev-verify-token&hub.challenge=abc");
    ok.StatusCode.Should().Be(HttpStatusCode.OK);
    (await ok.Content.ReadAsStringAsync()).Should().Contain("abc");
    (await anon.GetAsync("/api/v1/Strava/webhook?hub.mode=subscribe&hub.verify_token=nope&hub.challenge=abc")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task Deauthorize_event_removes_the_connection_and_strava_bindings()
  {
    var u = TestTokens.NewUser();
    var athlete = await this.Connect(u);
    var h = await this.BindStrava(u, "Run");
    await this.host.Anonymous().SendAsync(Webhook(Event("athlete", "update", athlete, athlete, new { authorized = "false" })));
    await this.Drain();
    (await app.Count("SELECT count(*) FROM proof.\"StravaConnections\" WHERE \"UserId\" = @u", ("u", u))).Should().Be(0);
    (await app.Count("SELECT count(*) FROM proof.\"ProofBindings\" WHERE \"HabitId\" = @h", ("h", h))).Should().Be(0);
    (await this.host.As(u).GetFromJsonAsync<JsonElement>($"/api/v1/Proof/{u}/{h}")).GetProperty("pluginKey").GetString().Should().Be("manual");
  }

  [Fact]
  public async Task Strava_endpoints_are_404_when_it_is_off()
  {
    var off = ProofHost.StravaOff(app);
    var u = TestTokens.NewUser();
    (await off.As(u).GetAsync($"/api/v1/Strava/{u}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await off.As(u).GetAsync($"/api/v1/Strava/connect-url/{u}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await off.Anonymous().GetAsync("/api/v1/Strava/webhook?hub.mode=subscribe&hub.verify_token=dev-verify-token&hub.challenge=a")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await off.Anonymous().SendAsync(Webhook(Event("activity", "create", 1, 2)))).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }
}
