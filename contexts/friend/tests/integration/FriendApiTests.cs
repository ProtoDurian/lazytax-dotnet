using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ctx.Friend.Contract.V1;
using Ctx.Notification.Contract.V1;
using LazyTax.Platform.Lifecycle;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Friend.Tests.Integration;

public sealed class FriendApiTests(KernelApp app)
{
  private static string Rel(JsonElement e) => e.GetProperty("relation").GetString()!;

  private static Task<HttpResponseMessage> Ask(HttpClient c, string from, string to) =>
    c.PostAsJsonAsync($"/api/v1/Friend/{from}/requests", new { toUserId = to });

  private async Task<(string a, string b)> BeFriends()
  {
    var a = await app.NewUser("alice");
    var b = await app.NewUser("bob");
    (await Ask(app.ClientAs(a), a, b)).StatusCode.Should().Be(HttpStatusCode.OK);
    (await app.ClientAs(b).PostAsync($"/api/v1/Friend/{b}/requests/{a}/accept", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    return (a, b);
  }

  [Fact]
  public async Task Request_accept_then_both_list_each_other()
  {
    var a = await app.NewUser("alice");
    var b = await app.NewUser("bob");

    var sent = await Ask(app.ClientAs(a), a, b);
    sent.StatusCode.Should().Be(HttpStatusCode.OK);
    Rel(await sent.Content.ReadFromJsonAsync<JsonElement>()).Should().Be("outgoing");

    var reqB = await (await app.ClientAs(b).GetAsync($"/api/v1/Friend/{b}/requests")).Content.ReadFromJsonAsync<JsonElement>();
    reqB.GetProperty("incoming").GetArrayLength().Should().Be(1);
    reqB.GetProperty("incoming")[0].GetProperty("userId").GetString().Should().Be(a);
    var reqA = await (await app.ClientAs(a).GetAsync($"/api/v1/Friend/{a}/requests")).Content.ReadFromJsonAsync<JsonElement>();
    reqA.GetProperty("outgoing")[0].GetProperty("userId").GetString().Should().Be(b);

    var accepted = await app.ClientAs(b).PostAsync($"/api/v1/Friend/{b}/requests/{a}/accept", null);
    accepted.StatusCode.Should().Be(HttpStatusCode.OK);
    (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString().Should().Be(a);

    foreach (var (me, other) in new[] { (a, b), (b, a) })
    {
      var list = await (await app.ClientAs(me).GetAsync($"/api/v1/Friend/{me}")).Content.ReadFromJsonAsync<JsonElement>();
      list.GetArrayLength().Should().Be(1);
      list[0].GetProperty("userId").GetString().Should().Be(other);
      list[0].GetProperty("username").GetString().Should().Be(await app.UsernameOf(other));
    }

    (await app.ClientAs(a).PostAsync($"/api/v1/Friend/{a}/requests/{b}/accept", null)).StatusCode
      .Should().Be(HttpStatusCode.NotFound, "the request is gone once accepted");
  }

  [Fact]
  public async Task Asking_someone_who_asked_you_makes_you_friends()
  {
    var a = await app.NewUser();
    var b = await app.NewUser();
    await Ask(app.ClientAs(a), a, b);
    var back = await Ask(app.ClientAs(b), b, a);
    Rel(await back.Content.ReadFromJsonAsync<JsonElement>()).Should().Be("friend");
    (await app.ClientAs(a).GetFromJsonAsync<JsonElement>($"/api/v1/Friend/{a}")).GetArrayLength().Should().Be(1);
    var again = await Ask(app.ClientAs(a), a, b);
    (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("relation").GetString()
      .Should().Be("friend", "asking a friend is a no-op");
  }

  [Fact]
  public async Task Asking_twice_keeps_one_request()
  {
    var a = await app.NewUser();
    var b = await app.NewUser();
    await Ask(app.ClientAs(a), a, b);
    Rel(await (await Ask(app.ClientAs(a), a, b)).Content.ReadFromJsonAsync<JsonElement>()).Should().Be("outgoing");
    (await app.Count("""SELECT count(*) FROM friend."FriendRequests" WHERE "FromUserId" = @a""", ("a", a))).Should().Be(1);
  }

  [Fact]
  public async Task Cannot_friend_yourself_or_a_missing_user()
  {
    var a = await app.NewUser();
    (await Ask(app.ClientAs(a), a, a)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await Ask(app.ClientAs(a), a, "u_nobody")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await app.ClientAs(a).PostAsJsonAsync($"/api/v1/Friend/{a}/requests", new { toUserId = "" })).StatusCode
      .Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task Search_matches_prefix_literally_and_shows_the_relation()
  {
    var a = await app.NewUser("seeker");
    var tag = "zq" + Guid.NewGuid().ToString("N")[..6];
    var b = await app.NewUser(tag + "_one");
    var c = await app.NewUser(tag + "_two");
    await Ask(app.ClientAs(a), a, b);

    var res = await app.ClientAs(a).GetFromJsonAsync<JsonElement>($"/api/v1/Friend/{a}/search?q={tag.ToUpperInvariant()}");
    res.GetArrayLength().Should().Be(2, "case-insensitive prefix");
    res.EnumerateArray().Single(x => x.GetProperty("userId").GetString() == b).GetProperty("relation").GetString().Should().Be("outgoing");
    res.EnumerateArray().Single(x => x.GetProperty("userId").GetString() == c).GetProperty("relation").GetString().Should().Be("none");

    var wild = await app.ClientAs(a).GetFromJsonAsync<JsonElement>($"/api/v1/Friend/{a}/search?q=%25%25");
    wild.GetArrayLength().Should().Be(0, "a % in the query matches literally");

    var self = await app.ClientAs(b).GetFromJsonAsync<JsonElement>($"/api/v1/Friend/{b}/search?q={tag}&limit=1");
    self.GetArrayLength().Should().Be(1);
    self[0].GetProperty("userId").GetString().Should().Be(c, "the caller is excluded");
  }

  [Fact]
  public async Task Search_validates_query_and_limit()
  {
    var a = await app.NewUser();
    (await app.ClientAs(a).GetAsync($"/api/v1/Friend/{a}/search?q=a")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await app.ClientAs(a).GetAsync($"/api/v1/Friend/{a}/search?q=abc&limit=21")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task Cancel_or_decline_a_request()
  {
    var a = await app.NewUser();
    var b = await app.NewUser();
    var c = await app.NewUser();
    await Ask(app.ClientAs(a), a, b);
    (await app.ClientAs(a).DeleteAsync($"/api/v1/Friend/{a}/requests/{b}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await app.ClientAs(a).DeleteAsync($"/api/v1/Friend/{a}/requests/{b}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

    await Ask(app.ClientAs(c), c, b);
    (await app.ClientAs(b).DeleteAsync($"/api/v1/Friend/{b}/requests/{c}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await app.ClientAs(b).GetFromJsonAsync<JsonElement>($"/api/v1/Friend/{b}/requests")).GetProperty("incoming").GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task Remove_ends_the_friendship_both_ways()
  {
    var (a, b) = await BeFriends();
    (await app.ClientAs(a).DeleteAsync($"/api/v1/Friend/{a}/{b}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await app.ClientAs(b).GetFromJsonAsync<JsonElement>($"/api/v1/Friend/{b}")).GetArrayLength().Should().Be(0);
    (await app.ClientAs(a).DeleteAsync($"/api/v1/Friend/{a}/{b}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task Invite_is_permanent_and_accepting_it_sends_a_request()
  {
    var a = await app.NewUser();
    var b = await app.NewUser();
    var first = await (await app.ClientAs(a).PostAsync($"/api/v1/Friend/{a}/invites", null)).Content.ReadFromJsonAsync<JsonElement>();
    var again = await (await app.ClientAs(a).PostAsync($"/api/v1/Friend/{a}/invites", null)).Content.ReadFromJsonAsync<JsonElement>();
    var code = first.GetProperty("code").GetString()!;
    again.GetProperty("code").GetString().Should().Be(code);
    code.Should().HaveLength(10);

    (await app.ClientAs(a).PostAsync($"/api/v1/Friend/{a}/invites/{code}/accept", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var res = await app.ClientAs(b).PostAsync($"/api/v1/Friend/{b}/invites/{code.ToUpperInvariant()}/accept", null);
    res.StatusCode.Should().Be(HttpStatusCode.OK);
    Rel(await res.Content.ReadFromJsonAsync<JsonElement>()).Should().Be("outgoing");
    (await app.ClientAs(b).PostAsync($"/api/v1/Friend/{b}/invites/nosuchcode/accept", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task Request_and_accept_notify_after_commit()
  {
    var a = await app.NewUser("alice");
    var b = await app.NewUser("bob");
    await Ask(app.ClientAs(a), a, b);
    await app.Dispatch();
    (await Unread(b)).Should().Be(1, "the person asked is told");

    await app.ClientAs(b).PostAsync($"/api/v1/Friend/{b}/requests/{a}/accept", null);
    await app.Dispatch();
    (await Unread(a)).Should().Be(1, "the requester is told it was accepted");
    await app.Dispatch();
    (await Unread(a)).Should().Be(1, "dispatching again does not double it");
  }

  [Fact]
  public async Task Contract_answers_AreFriends()
  {
    var (a, b) = await BeFriends();
    var c = await app.NewUser();
    await app.InScope(async sp =>
    {
      var friend = sp.GetRequiredService<IFriendV1>();
      (await friend.AreFriends(a, b)).Should().BeTrue();
      (await friend.AreFriends(b, a)).Should().BeTrue();
      (await friend.AreFriends(a, c)).Should().BeFalse();
      (await friend.ListFriendIds(a)).Should().Equal(b);
    });
  }

  [Fact]
  public async Task Purge_leaves_no_row_of_the_user()
  {
    var (a, b) = await BeFriends();
    var c = await app.NewUser();
    await Ask(app.ClientAs(c), c, a);
    await app.ClientAs(a).PostAsync($"/api/v1/Friend/{a}/invites", null);
    var habit = FakeHabit.Add(a);
    await app.WithFakeHabit().CreateClient().As(a).PutAsJsonAsync($"/api/v1/FriendStake/{a}/{habit}", new { friendUserIds = new[] { b } });

    await app.InScope(async sp =>
    {
      foreach (var p in sp.GetServices<IUserPurge>().Where(x => x.GetType().Namespace == "Ctx.Friend.Data"))
        await p.Purge(a, default);
    });

    foreach (var table in new[] { "FriendRequests|FromUserId|ToUserId", "Friendships|UserId|FriendUserId", "HabitFriendWatchers|OwnerUserId|FriendUserId" })
    {
      var p = table.Split('|');
      (await app.Count($"""SELECT count(*) FROM friend."{p[0]}" WHERE "{p[1]}" = @u OR "{p[2]}" = @u""", ("u", a))).Should().Be(0, p[0]);
    }
    (await app.Count("""SELECT count(*) FROM friend."FriendInvites" WHERE "UserId" = @u""", ("u", a))).Should().Be(0);
    (await app.Count("""SELECT count(*) FROM friend."Friendships" WHERE "UserId" = @u""", ("u", b))).Should().Be(0, "the friend's side is gone too");
  }

  private static Task<int> Unread(string userId) => Task.FromResult(FakeNotification.Of(userId).Count);
}
