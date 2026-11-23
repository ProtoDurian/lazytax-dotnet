using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ctx.Notification.Contract.V1;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Notification.Tests.Integration;

public sealed class InboxApiTests(KernelApp app)
{
  private static string Base(string user) => $"/api/v1/Inbox/{user}";

  private Task<InboxNotification> Post(string user, string title = "Hello", string? key = null, string? actor = null) =>
    app.InScope(sp => sp.GetRequiredService<INotificationV1>()
      .Post(user, InboxKinds.FriendRequest, title, "body", actor, "{\"a\":1}", key));

  [Fact]
  public async Task No_token_is_401()
  {
    var res = await app.Anonymous().GetAsync(Base(TestTokens.NewUser()));
    res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task Another_users_path_is_403()
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    foreach (var req in new[]
    {
      new HttpRequestMessage(HttpMethod.Get, Base(a)),
      new HttpRequestMessage(HttpMethod.Get, Base(a) + "/unread-count"),
      new HttpRequestMessage(HttpMethod.Post, Base(a) + $"/{Guid.NewGuid()}/read"),
      new HttpRequestMessage(HttpMethod.Post, Base(a) + "/read-all"),
    })
      (await app.ClientAs(b).SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.Forbidden, req.RequestUri!.ToString());
  }

  [Fact]
  public async Task Search_is_newest_first_and_paged()
  {
    var u = TestTokens.NewUser();
    for (var i = 1; i <= 3; i++) await this.Post(u, $"n{i}");
    var list = await app.ClientAs(u).GetFromJsonAsync<JsonElement>(Base(u));
    list.EnumerateArray().Select(x => x.GetProperty("title").GetString()).Should().Equal("n3", "n2", "n1");
    var page = await app.ClientAs(u).GetFromJsonAsync<JsonElement>(Base(u) + "?limit=1&skip=1");
    page.GetArrayLength().Should().Be(1);
    page[0].GetProperty("title").GetString().Should().Be("n2");
    page[0].GetProperty("data").GetString().Should().Be("{\"a\":1}");
  }

  [Theory]
  [InlineData("limit=0")]
  [InlineData("limit=51")]
  [InlineData("skip=-1")]
  public async Task Bad_paging_is_400(string query)
  {
    var u = TestTokens.NewUser();
    (await app.ClientAs(u).GetAsync(Base(u) + "?" + query)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task Unread_count_mark_read_and_read_all()
  {
    var u = TestTokens.NewUser();
    var c = app.ClientAs(u);
    var first = await this.Post(u, "one");
    await this.Post(u, "two");
    await this.Post(u, "three");
    (await c.GetFromJsonAsync<JsonElement>(Base(u) + "/unread-count")).GetProperty("count").GetInt32().Should().Be(3);

    (await c.PostAsync(Base(u) + $"/{first.Id}/read", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await c.PostAsync(Base(u) + $"/{first.Id}/read", null)).StatusCode.Should().Be(HttpStatusCode.NoContent, "marking twice is fine");
    var unread = await c.GetFromJsonAsync<JsonElement>(Base(u) + "?unreadOnly=true");
    unread.GetArrayLength().Should().Be(2);

    var all = await (await c.PostAsync(Base(u) + "/read-all", null)).Content.ReadFromJsonAsync<JsonElement>();
    all.GetProperty("count").GetInt32().Should().Be(2);
    (await c.GetFromJsonAsync<JsonElement>(Base(u) + "/unread-count")).GetProperty("count").GetInt32().Should().Be(0);
  }

  [Fact]
  public async Task Posting_the_same_dedupe_key_twice_records_once()
  {
    var u = TestTokens.NewUser();
    var one = await this.Post(u, "x", key: "k1");
    var two = await this.Post(u, "x", key: "k1");
    two.Id.Should().Be(one.Id);
    var other = TestTokens.NewUser();
    (await this.Post(other, "x", key: "k1")).Id.Should().NotBe(one.Id, "the key is per user");
    (await app.ClientAs(u).GetFromJsonAsync<JsonElement>(Base(u))).GetArrayLength().Should().Be(1);
  }

  [Fact]
  public async Task Invalid_post_throws_400()
  {
    var act = () => this.Post(TestTokens.NewUser(), title: "");
    (await act.Should().ThrowAsync<LazyTax.Platform.Problems.ValidationFailed>()).Which.Status.Should().Be(400);
  }

  [Fact]
  public Task Other_user_gets_404_marking_a_notification_read() => OwnerIsolation.AssertOtherUserGets404(app,
    createAsA: async (_, a) => (await this.Post(a)).Id.ToString(),
    requestAsB: (b, id) => new HttpRequestMessage(HttpMethod.Post, Base(b) + $"/{id}/read"));

  [Fact]
  public async Task Mark_read_of_someone_elses_row_changes_nothing()
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    var n = await this.Post(a);
    (await app.ClientAs(b).PostAsync(Base(b) + $"/{n.Id}/read", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await app.ClientAs(a).GetFromJsonAsync<JsonElement>(Base(a) + "/unread-count")).GetProperty("count").GetInt32().Should().Be(1);
    (await app.ClientAs(b).GetFromJsonAsync<JsonElement>(Base(b))).GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task A_unknown_id_is_404()
  {
    var u = TestTokens.NewUser();
    (await app.ClientAs(u).PostAsync(Base(u) + $"/{Guid.NewGuid()}/read", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task Mcp_tools_use_the_same_rules()
  {
    var u = TestTokens.NewUser();
    var other = TestTokens.NewUser();
    var n = await this.Post(u, "from mcp");
    var c = app.ClientAs(u);

    async Task<JsonElement> Call(HttpClient client, string tool, object args) =>
      await (await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = args } }))
        .Content.ReadFromJsonAsync<JsonElement>();

    var list = await Call(c, "notification.list", new { userId = u });
    list.GetProperty("result").GetProperty("isError").GetBoolean().Should().BeFalse();
    list.GetProperty("result").GetProperty("structuredContent")[0].GetProperty("title").GetString().Should().Be("from mcp");

    (await Call(c, "notification.unreadCount", new { userId = u })).GetProperty("result").GetProperty("structuredContent")
      .GetProperty("count").GetInt32().Should().Be(1);

    var forbidden = await Call(app.ClientAs(other), "notification.list", new { userId = u });
    (forbidden.TryGetProperty("error", out _) || forbidden.GetProperty("result").GetProperty("isError").GetBoolean()).Should().BeTrue();

    var notMine = await Call(app.ClientAs(other), "notification.markRead", new { userId = other, id = n.Id });
    notMine.GetProperty("result").GetProperty("isError").GetBoolean().Should().BeTrue();

    (await Call(c, "notification.markRead", new { userId = u, id = n.Id })).GetProperty("result").GetProperty("isError").GetBoolean().Should().BeFalse();
    (await Call(c, "notification.markAllRead", new { userId = u })).GetProperty("result").GetProperty("structuredContent")
      .GetProperty("count").GetInt32().Should().Be(0);
  }
}
