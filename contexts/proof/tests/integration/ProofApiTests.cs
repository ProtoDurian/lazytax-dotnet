using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ctx.Proof.Contract.V1;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Proof.Tests.Integration;

public sealed class ProofApiTests(KernelApp app)
{
  private readonly ProofHost host = ProofHost.On(app);
  private const string Settings = """{"sportTypes":["Run"]}""";

  private async Task<string> ConnectStrava(string user)
  {
    var c = this.host.NoRedirect();
    var url = (await (await this.host.As(user).GetAsync($"/api/v1/Strava/connect-url/{user}")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
    var state = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["state"]!;
    var res = await c.GetAsync($"/api/v1/Strava/callback?code=athlete-{Math.Abs(user.GetHashCode()) + 1000}&scope=read,activity:read_all&state={Uri.EscapeDataString(state)}");
    res.StatusCode.Should().Be(HttpStatusCode.Redirect);
    return res.Headers.Location!.Query;
  }

  [Fact]
  public async Task Plugins_lists_manual_nfc_and_strava_with_their_tiers()
  {
    var res = await this.host.As(TestTokens.NewUser()).GetAsync("/api/v1/Proof/plugins");
    res.StatusCode.Should().Be(HttpStatusCode.OK);
    var list = await res.Content.ReadFromJsonAsync<List<JsonElement>>();
    list!.Select(p => p.GetProperty("key").GetString()).Should().Equal("manual", "nfc", "strava");
    list!.Single(p => p.GetProperty("key").GetString() == "strava").GetProperty("minTier").GetString().Should().Be("pro");
  }

  [Fact]
  public async Task Strava_is_left_out_of_plugins_when_it_is_off()
  {
    var off = ProofHost.StravaOff(app);
    var list = await (await off.As(TestTokens.NewUser()).GetAsync("/api/v1/Proof/plugins")).Content.ReadFromJsonAsync<List<JsonElement>>();
    list!.Select(p => p.GetProperty("key").GetString()).Should().Equal("manual", "nfc");
  }

  [Fact]
  public async Task Get_without_a_binding_reports_manual()
  {
    var u = TestTokens.NewUser();
    var h = this.host.Habits.Add(u);
    var body = await (await this.host.As(u).GetAsync($"/api/v1/Proof/{u}/{h}")).Content.ReadFromJsonAsync<JsonElement>();
    body.GetProperty("pluginKey").GetString().Should().Be("manual");
    body.GetProperty("settings").GetString().Should().Be("{}");
  }

  [Fact]
  public async Task Bind_get_rebind_unbind_round_trip()
  {
    var u = TestTokens.NewUser();
    this.host.Plan.Pro.Add(u);
    var h = this.host.Habits.Add(u);
    var client = this.host.As(u);
    (await this.ConnectStrava(u)).Should().Contain("result=connected");

    var put = await client.PutAsJsonAsync($"/api/v1/Proof/{u}/{h}", new { pluginKey = "strava", settings = " { \"sportTypes\" : [\"Run\"] } " });
    put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
    (await put.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("settings").GetString().Should().Be("""{"sportTypes":["Run"]}""");
    (await client.GetFromJsonAsync<JsonElement>($"/api/v1/Proof/{u}/{h}")).GetProperty("pluginKey").GetString().Should().Be("strava");

    (await client.PutAsJsonAsync($"/api/v1/Proof/{u}/{h}", new { pluginKey = "strava", settings = """{"sportTypes":["Ride"]}""" })).StatusCode.Should().Be(HttpStatusCode.OK);
    (await app.Count("SELECT count(*) FROM proof.\"ProofBindings\" WHERE \"HabitId\" = @h", ("h", h))).Should().Be(1);

    (await client.DeleteAsync($"/api/v1/Proof/{u}/{h}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await client.DeleteAsync($"/api/v1/Proof/{u}/{h}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await client.GetFromJsonAsync<JsonElement>($"/api/v1/Proof/{u}/{h}")).GetProperty("pluginKey").GetString().Should().Be("manual");
  }

  [Fact]
  public async Task Bind_refusals_are_400s()
  {
    var u = TestTokens.NewUser();
    var h = this.host.Habits.Add(u);
    var client = this.host.As(u);
    // free tier
    (await client.PutAsJsonAsync($"/api/v1/Proof/{u}/{h}", new { pluginKey = "strava", settings = Settings })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    this.host.Plan.Pro.Add(u);
    // pro, not connected
    (await client.PutAsJsonAsync($"/api/v1/Proof/{u}/{h}", new { pluginKey = "strava", settings = Settings })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    // always bound plugin, bad settings, empty request
    (await client.PutAsJsonAsync($"/api/v1/Proof/{u}/{h}", new { pluginKey = "manual", settings = "{}" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await client.PutAsJsonAsync($"/api/v1/Proof/{u}/{h}", new { pluginKey = "strava", settings = "{" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await client.PutAsJsonAsync($"/api/v1/Proof/{u}/{h}", new { pluginKey = "", settings = "{}" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await client.PutAsJsonAsync($"/api/v1/Proof/{u}/{h}", new { pluginKey = "nope", settings = "{}" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await app.Count("SELECT count(*) FROM proof.\"ProofBindings\" WHERE \"UserId\" = @u", ("u", u))).Should().Be(0);
  }

  [Fact]
  public async Task Anonymous_is_401_and_another_users_path_is_403()
  {
    var u = TestTokens.NewUser();
    var h = this.host.Habits.Add(u);
    (await this.host.Anonymous().GetAsync($"/api/v1/Proof/{u}/{h}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await this.host.Anonymous().GetAsync("/api/v1/Proof/plugins")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await this.host.As(TestTokens.NewUser()).GetAsync($"/api/v1/Proof/{u}/{h}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await this.host.As(TestTokens.NewUser()).DeleteAsync($"/api/v1/Proof/{u}/{h}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task Other_user_gets_404_for_a_binding_and_for_a_habit_that_is_not_theirs()
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    this.host.Plan.Pro.Add(a);
    var ha = this.host.Habits.Add(a);
    await this.ConnectStrava(a);
    (await this.host.As(a).PutAsJsonAsync($"/api/v1/Proof/{a}/{ha}", new { pluginKey = "strava", settings = Settings })).StatusCode.Should().Be(HttpStatusCode.OK);

    // B, with B's own userId in the path and A's habit id
    (await this.host.As(b).GetAsync($"/api/v1/Proof/{b}/{ha}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await this.host.As(b).DeleteAsync($"/api/v1/Proof/{b}/{ha}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    this.host.Plan.Pro.Add(b);
    (await this.host.As(b).PutAsJsonAsync($"/api/v1/Proof/{b}/{ha}", new { pluginKey = "strava", settings = Settings })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    // A's binding is untouched
    (await app.Count("SELECT count(*) FROM proof.\"ProofBindings\" WHERE \"HabitId\" = @h AND \"UserId\" = @u", ("h", ha), ("u", a))).Should().Be(1);
  }

  [Fact]
  public async Task The_contract_routes_an_event_and_lists_bindings_owner_scoped()
  {
    var u = TestTokens.NewUser();
    var h = this.host.Habits.Add(u);
    var outcomes = await this.host.InScope(sp => sp.GetRequiredService<IProofV1>()
      .Route(new ProofEvent(ProofPlugins.Manual, u, "ext-1", DateTime.UtcNow, h), default));
    outcomes.Should().ContainSingle().Which.ExecutionId.Should().NotBeNull();
    this.host.Check.Reports.Should().Contain(r => r.HabitId == h && r.Evidence.ProofKey == "manual");

    var keys = await this.host.InScope(sp => sp.GetRequiredService<IProofV1>().ListPluginKeys(LazyTax.Platform.Data.Owner.User(TestTokens.NewUser()), default));
    keys.Should().BeEmpty();
  }

  [Fact]
  public async Task Mcp_tools_exist_and_call_the_same_service()
  {
    var u = TestTokens.NewUser();
    var h = this.host.Habits.Add(u);
    var client = this.host.As(u);
    var list = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
    (await list.Content.ReadAsStringAsync()).Should().Contain("proof.bind").And.Contain("proof.nfcTap");
    var call = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = "proof.binding", arguments = new { userId = u, habitId = h.ToString() } } });
    (await call.Content.ReadAsStringAsync()).Should().Contain("manual");
    var other = await this.host.As(TestTokens.NewUser()).PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "proof.binding", arguments = new { userId = u, habitId = h.ToString() } } });
    (await other.Content.ReadAsStringAsync()).Should().NotContain("\"pluginKey\"");
  }
}
