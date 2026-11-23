using System.Net;
using System.Net.Http.Json;
using Ctx.Vacation.Contract.V1;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Vacation.Tests.Integration;

public sealed class VacationApiTests(KernelApp app)
{
  private static object Window(string start, string end) => new { startDate = start, endDate = end, timezone = "UTC" };

  private async Task<string> ProUser()
  {
    var user = await app.SignUp();
    await app.SetTier(user, "pro");
    return user;
  }

  [Fact]
  public async Task Create_list_and_is_away()
  {
    var user = await this.ProUser();
    var client = app.ClientAs(user);
    var v = await client.PostAsJsonAsync($"/api/v1/Vacation/{user}", Window("10-01-2099", "12-01-2099")).Expect(HttpStatusCode.OK);
    v.GetProperty("startDate").GetString().Should().Be("10-01-2099");
    var list = await client.GetAsync($"/api/v1/Vacation/{user}?year=2099").Expect(HttpStatusCode.OK);
    list.GetArrayLength().Should().Be(1);
    (await app.InScope(sp => sp.GetRequiredService<IVacationV1>().IsAway(user, new DateOnly(2099, 1, 11)))).Should().BeTrue();
    (await app.InScope(sp => sp.GetRequiredService<IVacationV1>().IsAway(user, new DateOnly(2099, 1, 13)))).Should().BeFalse();
  }

  [Fact]
  public async Task Free_tier_stops_at_the_plan_guard()
  {
    var user = await app.SignUp();
    var body = await app.ClientAs(user).PostAsJsonAsync($"/api/v1/Vacation/{user}", Window("10-01-2099", "12-01-2099")).Expect(HttpStatusCode.BadRequest);
    body.GetProperty("type").GetString().Should().Be("/problems/v1/tier_insufficient");
  }

  [Fact]
  public async Task Overlap_is_400()
  {
    var user = await this.ProUser();
    var client = app.ClientAs(user);
    await client.PostAsJsonAsync($"/api/v1/Vacation/{user}", Window("10-01-2099", "12-01-2099")).Expect(HttpStatusCode.OK);
    await client.PostAsJsonAsync($"/api/v1/Vacation/{user}", Window("12-01-2099", "14-01-2099")).Expect(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task Window_quota_holds_under_concurrent_creates()
  {
    var user = await this.ProUser(); // pro: 6 windows a year
    var client = app.ClientAs(user);
    var calls = Enumerable.Range(1, 8).Select(i =>
      client.PostAsJsonAsync($"/api/v1/Vacation/{user}", Window($"{i:00}-03-2099", $"{i:00}-03-2099")));
    var results = await Task.WhenAll(calls);
    results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(6);
    (await app.Count("SELECT count(*) FROM vacation.\"VacationPeriods\" WHERE \"UserId\" = @u", ("u", user))).Should().Be(6);
  }

  [Fact]
  public Task Another_users_vacation_is_404_on_delete() => OwnerIsolation.AssertOtherUserGets404(app,
    createAsA: async (client, a) =>
    {
      await app.SignUp(a);
      await app.SetTier(a, "pro");
      var v = await client.PostAsJsonAsync($"/api/v1/Vacation/{a}", Window("10-01-2099", "12-01-2099")).Expect(HttpStatusCode.OK);
      return v.GetProperty("id").GetString()!;
    },
    requestAsB: (b, id) => new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/Vacation/{b}/{id}"));

  [Fact]
  public Task Another_users_vacation_is_404_on_end_today() => OwnerIsolation.AssertOtherUserGets404(app,
    createAsA: async (client, a) =>
    {
      await app.SignUp(a);
      await app.SetTier(a, "pro");
      var v = await client.PostAsJsonAsync($"/api/v1/Vacation/{a}", Window("10-01-2099", "12-01-2099")).Expect(HttpStatusCode.OK);
      return v.GetProperty("id").GetString()!;
    },
    requestAsB: (b, id) => new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/Vacation/{b}/{id}/end-today"));

  [Fact]
  public async Task Delete_a_future_vacation()
  {
    var user = await this.ProUser();
    var client = app.ClientAs(user);
    var v = await client.PostAsJsonAsync($"/api/v1/Vacation/{user}", Window("10-01-2099", "12-01-2099")).Expect(HttpStatusCode.OK);
    await client.DeleteAsync($"/api/v1/Vacation/{user}/{v.GetProperty("id").GetString()}").Expect(HttpStatusCode.NoContent);
  }

  [Fact]
  public async Task Another_users_path_is_403() =>
    await app.ClientAs(TestTokens.NewUser()).GetAsync($"/api/v1/Vacation/{TestTokens.NewUser()}").Expect(HttpStatusCode.Forbidden);

  [Fact]
  public Task No_token_is_401() => app.Anonymous().GetAsync($"/api/v1/Vacation/{TestTokens.NewUser()}").Expect(HttpStatusCode.Unauthorized);

  [Fact]
  public async Task Mcp_create_runs_the_same_guard_and_subject()
  {
    var free = await app.SignUp();
    var denied = await app.ClientAs(free).CallTool("vacation.create", new { userId = free, startDate = "10-01-2099", endDate = "12-01-2099", timezone = "UTC" });
    denied.GetProperty("structuredContent").GetProperty("type").GetString().Should().Be("/problems/v1/tier_insufficient");

    var pro = await this.ProUser();
    var ok = await app.ClientAs(pro).CallTool("vacation.create", new { userId = pro, startDate = "10-01-2099", endDate = "12-01-2099", timezone = "UTC" });
    ok.GetProperty("isError").GetBoolean().Should().BeFalse();

    var other = await app.ClientAs(free).CallTool("vacation.create", new { userId = pro, startDate = "10-02-2099", endDate = "12-02-2099", timezone = "UTC" });
    other.GetProperty("structuredContent").GetProperty("status").GetInt32().Should().Be(403);
  }
}
