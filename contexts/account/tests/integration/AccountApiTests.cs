using System.Net;
using System.Net.Http.Json;
using LazyTax.Testing;

namespace Ctx.Account.Tests.Integration;

public sealed class AccountApiTests(KernelApp app)
{
  [Fact]
  public async Task Sign_up_then_read_myself()
  {
    var user = await app.SignUp();
    var me = await app.ClientAs(user).GetAsync("/api/v1/User/Me/All").Expect(HttpStatusCode.OK);
    me.GetProperty("principal").GetProperty("id").GetString().Should().Be(user);
    var cfg = await app.ClientAs(user).GetAsync("/api/v1/Configuration/me").Expect(HttpStatusCode.OK);
    cfg.GetProperty("principal").GetProperty("timezone").GetString().Should().Be("UTC");
  }

  [Fact]
  public async Task Sign_up_with_tokens_of_another_sub_is_refused()
  {
    var user = TestTokens.NewUser();
    var other = TestTokens.NewUser();
    var body = await app.ClientAs(user).PostAsJsonAsync("/api/v1/User", new
    {
      idToken = TestTokens.IdToken(other, "x" + other, "x@example.test"),
      accessToken = TestTokens.For(other),
    }).Expect(HttpStatusCode.BadRequest);
    body.GetProperty("type").GetString().Should().Be("/problems/v1/invalid_user_token");
  }

  [Fact]
  public Task No_token_is_401() => app.Anonymous().GetAsync("/api/v1/User/Me/All").Expect(HttpStatusCode.Unauthorized);

  [Fact]
  public async Task Another_users_path_is_403()
  {
    var a = await app.SignUp();
    await app.ClientAs(TestTokens.NewUser()).GetAsync($"/api/v1/User/{a}").Expect(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task Admin_reads_any_user_and_searches()
  {
    var a = await app.SignUp();
    var admin = app.ClientAs("root", "admin");
    await admin.GetAsync($"/api/v1/User/{a}").Expect(HttpStatusCode.OK);
    var found = await admin.GetAsync($"/api/v1/User?id={a}").Expect(HttpStatusCode.OK);
    found.GetArrayLength().Should().Be(1);
  }

  [Fact]
  public async Task Search_is_admin_only() =>
    await app.ClientAs(await app.SignUp()).GetAsync("/api/v1/User").Expect(HttpStatusCode.Forbidden);

  [Fact]
  public Task Configuration_of_another_user_is_404() => OwnerIsolation.AssertOtherUserGets404(app,
    createAsA: async (client, a) =>
    {
      await app.SignUp(a);
      var cfg = await client.GetAsync("/api/v1/Configuration/me").Expect(HttpStatusCode.OK);
      return cfg.GetProperty("principal").GetProperty("id").GetString()!;
    },
    requestAsB: (b, id) => new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Configuration/{b}/{id}"));

  [Fact]
  public Task Updating_another_users_configuration_is_404() => OwnerIsolation.AssertOtherUserGets404(app,
    createAsA: async (client, a) =>
    {
      await app.SignUp(a);
      var cfg = await client.GetAsync("/api/v1/Configuration/me").Expect(HttpStatusCode.OK);
      return cfg.GetProperty("principal").GetProperty("id").GetString()!;
    },
    requestAsB: (b, id) => new HttpRequestMessage(HttpMethod.Put, $"/api/v1/Configuration/{id}") { Content = JsonContent.Create(new { timezone = "UTC" }) });

  [Fact]
  public async Task Timezone_change_emits_an_outbox_event()
  {
    var user = await app.SignUp();
    var client = app.ClientAs(user);
    var id = (await client.GetAsync("/api/v1/Configuration/me").Expect(HttpStatusCode.OK)).GetProperty("principal").GetProperty("id").GetString();
    var res = await client.PutAsJsonAsync($"/api/v1/Configuration/{user}/{id}", new { timezone = "Asia/Singapore" }).Expect(HttpStatusCode.OK);
    res.GetProperty("timezone").GetString().Should().Be("Asia/Singapore");
    (await app.Count("SELECT count(*) FROM kernel.outbox_events WHERE name = 'account.timezone_changed' AND payload->>'userId' = @u", ("u", user)))
      .Should().Be(1);
  }

  [Fact]
  public async Task Bad_timezone_is_400() =>
    await app.ClientAs(TestTokens.NewUser()).PostAsJsonAsync("/api/v1/Configuration", new { timezone = "Mars/Base" }).Expect(HttpStatusCode.BadRequest);

  [Fact]
  public async Task Mcp_get_user_uses_the_same_rules()
  {
    var a = await app.SignUp();
    var ok = await app.ClientAs(a).CallTool("account.get_user", new { userId = a });
    ok.GetProperty("isError").GetBoolean().Should().BeFalse();
    var denied = await app.ClientAs(TestTokens.NewUser()).CallTool("account.get_user", new { userId = a });
    denied.GetProperty("isError").GetBoolean().Should().BeTrue();
    denied.GetProperty("structuredContent").GetProperty("status").GetInt32().Should().Be(403);
  }
}
