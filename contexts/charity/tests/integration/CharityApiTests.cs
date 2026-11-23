#pragma warning disable xUnit1051 // HTTP test calls: no per-test cancellation needed
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ctx.Charity.Contract.V1;
using LazyTax.Platform.Lifecycle;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Charity.Tests.Integration;

public sealed class CharityApiTests(KernelApp app)
{
  private HttpClient Admin() => app.ClientAs(TestTokens.NewUser(), "admin");
  private HttpClient User() => app.ClientAs(TestTokens.NewUser());

  private static string Uniq() => Guid.NewGuid().ToString("N")[..10];

  private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

  private async Task<(string id, string name)> NewCharity(HttpClient admin, string[]? countries = null)
  {
    var name = "Charity " + Uniq();
    var r = await admin.PostAsJsonAsync("/api/v1/Charity", new { name, countries = countries ?? ["US"], primaryRegistrationCountry = "US" });
    r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
    return ((await Json(r)).GetProperty("id").GetString()!, name);
  }

  [Fact]
  public async Task Anonymous_gets_401_on_read_and_write()
  {
    (await app.Anonymous().GetAsync("/api/v1/Charity")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await app.Anonymous().GetAsync("/api/v1/Causes")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await app.Anonymous().PostAsync("/api/v1/charities/sync/pledge", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task Non_admin_gets_403_on_every_write()
  {
    var u = User();
    var id = Guid.NewGuid();
    (await u.PostAsJsonAsync("/api/v1/Charity", new { name = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await u.PutAsJsonAsync($"/api/v1/Charity/{id}", new { name = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await u.DeleteAsync($"/api/v1/Charity/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await u.PutAsJsonAsync($"/api/v1/Charity/{id}/causes", new { keys = new string[0] })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await u.PostAsJsonAsync("/api/v1/Causes", new { key = "k", name = "n" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await u.DeleteAsync($"/api/v1/Causes/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await u.PostAsync("/api/v1/charities/sync/pledge", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task Unknown_ids_get_404()
  {
    var admin = Admin();
    var id = Guid.NewGuid();
    (await User().GetAsync($"/api/v1/Charity/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await User().GetAsync($"/api/v1/Causes/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await admin.PutAsJsonAsync($"/api/v1/Charity/{id}", new { name = "x" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await admin.DeleteAsync($"/api/v1/Charity/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await admin.PutAsJsonAsync($"/api/v1/Charity/{id}/causes", new { keys = new string[0] })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await admin.PutAsJsonAsync($"/api/v1/Causes/{id}", new { name = "x" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await admin.DeleteAsync($"/api/v1/Causes/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task Crud_search_and_get()
  {
    var admin = Admin();
    var (id, name) = await NewCharity(admin, ["GB"]);

    var got = await Json(await User().GetAsync($"/api/v1/Charity/{id}"));
    got.GetProperty("principal").GetProperty("name").GetString().Should().Be(name);

    var found = await Json(await User().GetAsync($"/api/v1/Charity?name={name}&country=GB"));
    found.GetArrayLength().Should().Be(1);

    var upd = await admin.PutAsJsonAsync($"/api/v1/Charity/{id}", new { name = name + " 2", countries = new[] { "GB" } });
    (await Json(upd)).GetProperty("name").GetString().Should().Be(name + " 2");

    (await admin.DeleteAsync($"/api/v1/Charity/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await User().GetAsync($"/api/v1/Charity/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task Validation_errors_are_400()
  {
    var admin = Admin();
    (await admin.PostAsJsonAsync("/api/v1/Charity", new { name = "x", countries = new[] { "USA" } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await User().GetAsync("/api/v1/Charity?limit=101")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await User().GetAsync("/api/v1/Charity?country=USA")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task Causes_crud_conflict_and_set_causes_with_search_by_cause()
  {
    var admin = Admin();
    var key = "cause-" + Uniq();
    var c = await admin.PostAsJsonAsync("/api/v1/Causes", new { key, name = "Water" });
    c.StatusCode.Should().Be(HttpStatusCode.OK);
    var causeId = (await Json(c)).GetProperty("id").GetString()!;
    (await admin.PostAsJsonAsync("/api/v1/Causes", new { key, name = "Again" })).StatusCode.Should().Be(HttpStatusCode.Conflict);

    var upd = await Json(await admin.PutAsJsonAsync($"/api/v1/Causes/{causeId}", new { name = "Clean water" }));
    upd.GetProperty("key").GetString().Should().Be(key);
    upd.GetProperty("name").GetString().Should().Be("Clean water");

    var (id, _) = await NewCharity(admin);
    (await admin.PutAsJsonAsync($"/api/v1/Charity/{id}/causes", new { keys = new[] { "nope-" + Uniq() } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await admin.PutAsJsonAsync($"/api/v1/Charity/{id}/causes", new { keys = new[] { key } })).StatusCode.Should().Be(HttpStatusCode.NoContent);

    var byCause = await Json(await User().GetAsync($"/api/v1/Charity?causeKey={key}"));
    byCause.GetArrayLength().Should().Be(1);
    byCause[0].GetProperty("id").GetString().Should().Be(id);

    // deleting the cause removes the link
    (await admin.DeleteAsync($"/api/v1/Causes/{causeId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await Json(await User().GetAsync($"/api/v1/Charity?causeKey={key}"))).GetArrayLength().Should().Be(0);

    // deleting the charity removes its links (cascade)
    (await admin.DeleteAsync($"/api/v1/Charity/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await app.Count("SELECT count(*) FROM charity.\"CharityCauses\" WHERE \"CharityId\" = @i", ("i", Guid.Parse(id)))).Should().Be(0);
  }

  [Fact]
  public async Task Supported_countries_is_served()
  {
    var list = await Json(await User().GetAsync("/api/v1/Charity/supported-countries"));
    list.EnumerateArray().Select(x => x.GetString()).Should().Contain(["US", "GB"]);
  }

  [Fact]
  public async Task Pledge_sync_fills_the_catalogue_through_the_fake_gateway_and_is_idempotent()
  {
    var admin = Admin();
    var first = await Json(await admin.PostAsync("/api/v1/charities/sync/pledge", null));
    first.GetProperty("charitiesProcessed").GetInt32().Should().BeGreaterThan(0);
    var second = await Json(await admin.PostAsync("/api/v1/charities/sync/pledge", null));
    second.GetProperty("charitiesCreated").GetInt32().Should().Be(0);
    second.GetProperty("causesUpserted").GetInt32().Should().Be(0);

    await app.InScope(async sp =>
    {
      var v1 = sp.GetRequiredService<ICharityV1>();
      var org = await v1.GetByExternalId("pledge", "org-us-1");
      org.Should().NotBeNull();
      org!.Name.Should().Be("Fake Hope Fund");
      (await v1.Exists(org.Id)).Should().BeTrue();
      (await v1.Get(org.Id))!.Slug.Should().Be("fake-hope-fund");
      (await v1.GetExternalKey(org.Id, "pledge")).Should().Be("org-us-1");
      (await v1.GetExternalKey(org.Id, "other")).Should().BeNull();
      (await v1.Search(new CharitySearchQuery { CauseKey = "2", Country = "US", Limit = 100 })).Should().Contain(c => c.Id == org.Id);
    });
  }

  [Fact]
  public async Task Contract_returns_null_and_false_for_unknown()
  {
    await app.InScope(async sp =>
    {
      var v1 = sp.GetRequiredService<ICharityV1>();
      (await v1.Get(Guid.NewGuid())).Should().BeNull();
      (await v1.Exists(Guid.NewGuid())).Should().BeFalse();
      (await v1.GetExternalKey(Guid.NewGuid(), "pledge")).Should().BeNull();
    });
  }

  [Fact]
  public async Task Mcp_tool_matches_the_route_and_needs_a_token()
  {
    var admin = Admin();
    var (_, name) = await NewCharity(admin);
    var body = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "charity.search", arguments = new { name } } });
    var anon = await app.Anonymous().PostAsync("/mcp", body);
    var anonBody = await anon.Content.ReadAsStringAsync();
    anonBody.Should().Contain("\"isError\":true").And.Contain("unauthenticated").And.NotContain(name);
    var body2 = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "charity.search", arguments = new { name } } });
    var res = await User().PostAsync("/mcp", body2);
    res.StatusCode.Should().Be(HttpStatusCode.OK);
    (await res.Content.ReadAsStringAsync()).Should().Contain(name);
  }

  [Fact]
  public async Task Purge_leaves_the_catalogue_alone_and_no_column_holds_a_user_id()
  {
    var (_, _) = await NewCharity(Admin());
    var before = await app.Count("SELECT count(*) FROM charity.\"Charities\"");
    var user = TestTokens.NewUser();
    await app.InScope(async sp =>
    {
      foreach (var p in sp.GetServices<IUserPurge>()) await p.Purge(user, default);
    });
    (await app.Count("SELECT count(*) FROM charity.\"Charities\"")).Should().Be(before);
    (await app.Count(
      "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'charity' AND column_name ILIKE '%user%'"))
      .Should().Be(0, "charity owns no user rows, so a purge has nothing to delete");
  }
}
