using System.Net;
using System.Net.Http.Json;
using LazyTax.Testing;

namespace Ctx.Friend.Tests.Integration;

/// <summary>Auth layers 1-4 on every friend entity: 401 without a token, 403 on another user's path, 404 on another user's row.</summary>
public sealed class FriendAuthTests(KernelApp app)
{
  public static TheoryData<string, string> Routes => new()
  {
    { "GET", "/api/v1/Friend/{u}" },
    { "GET", "/api/v1/Friend/{u}/search?q=abc" },
    { "GET", "/api/v1/Friend/{u}/requests" },
    { "POST", "/api/v1/Friend/{u}/requests" },
    { "POST", "/api/v1/Friend/{u}/invites" },
    { "POST", "/api/v1/Friend/{u}/invites/abc/accept" },
    { "DELETE", "/api/v1/Friend/{u}/x" },
    { "GET", "/api/v1/FriendStake/{u}/00000000-0000-0000-0000-000000000001" },
    { "PUT", "/api/v1/FriendStake/{u}/00000000-0000-0000-0000-000000000001" },
  };

  private static HttpRequestMessage Req(string method, string path, string u) =>
    new(new HttpMethod(method), path.Replace("{u}", u))
    {
      Content = method is "POST" or "PUT" ? JsonContent.Create(new { toUserId = "x", friendUserIds = Array.Empty<string>() }) : null,
    };

  [Theory]
  [MemberData(nameof(Routes))]
  public async Task No_token_is_401(string method, string path)
  {
    var u = TestTokens.NewUser();
    (await app.Anonymous().SendAsync(Req(method, path, u))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Theory]
  [MemberData(nameof(Routes))]
  public async Task Another_users_path_is_403(string method, string path)
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    (await app.ClientAs(b).SendAsync(Req(method, path, a))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  // Entity: friendship. A and C are friends; B, with their own userId in the path, cannot end it.
  [Fact]
  public Task Friendship_of_another_user_is_404() => OwnerIsolation.AssertOtherUserGets404(app,
    createAsA: async (client, a) =>
    {
      var c = await app.NewUser();
      await client.PostAsJsonAsync($"/api/v1/Friend/{a}/requests", new { toUserId = c });
      await app.ClientAs(c).PostAsync($"/api/v1/Friend/{c}/requests/{a}/accept", null);
      return c;
    },
    requestAsB: (b, c) => new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/Friend/{b}/{c}"));

  // Entity: friend request. A asked C; B cannot decline it, and cannot accept "C's request" either.
  [Fact]
  public Task Request_of_another_user_is_404_to_delete() => OwnerIsolation.AssertOtherUserGets404(app,
    createAsA: async (client, a) =>
    {
      var c = await app.NewUser();
      await client.PostAsJsonAsync($"/api/v1/Friend/{a}/requests", new { toUserId = c });
      return c;
    },
    requestAsB: (b, c) => new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/Friend/{b}/requests/{c}"));

  [Fact]
  public Task Request_of_another_user_is_404_to_accept() => OwnerIsolation.AssertOtherUserGets404(app,
    createAsA: async (client, a) =>
    {
      var c = await app.NewUser();
      await app.ClientAs(c).PostAsJsonAsync($"/api/v1/Friend/{c}/requests", new { toUserId = a });
      return c;
    },
    requestAsB: (b, c) => new HttpRequestMessage(HttpMethod.Post, $"/api/v1/Friend/{b}/requests/{c}/accept"));

  // Entity: friend-stake watchers. A's habit is invisible to B, on read and on write.
  [Fact]
  public async Task Watchers_of_another_users_habit_are_404()
  {
    var habits = app.WithFakeHabit();
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    var habit = FakeHabit.Add(a);
    (await habits.CreateClient().As(b).GetAsync($"/api/v1/FriendStake/{b}/{habit}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await habits.CreateClient().As(b).PutAsJsonAsync($"/api/v1/FriendStake/{b}/{habit}", new { friendUserIds = Array.Empty<string>() }))
      .StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await app.Count("""SELECT count(*) FROM friend."HabitFriendWatchers" WHERE "HabitId" = @h""", ("h", habit))).Should().Be(0);
  }
}
