using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ctx.Notification.Contract.V1;
using LazyTax.Kernel.Outbox;
using LazyTax.Platform.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using LazyTax.Platform.Outbox;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Friend.Tests.Integration;

public sealed class FriendStakeTests(KernelApp app)
{
  private readonly WebApplicationFactory<Program> host = app.WithFakeHabit();

  private HttpClient As(string user) => host.CreateClient().As(user);

  private Task Dispatch() => host.Services.GetRequiredService<IOutboxDispatcher>().DispatchOnce();

  private async Task Befriend(string a, string b)
  {
    await app.ClientAs(a).PostAsJsonAsync($"/api/v1/Friend/{a}/requests", new { toUserId = b });
    await app.ClientAs(b).PostAsync($"/api/v1/Friend/{b}/requests/{a}/accept", null);
  }

  private static Task<HttpResponseMessage> Put(HttpClient c, string u, Guid habit, params string[] friends) =>
    c.PutAsJsonAsync($"/api/v1/FriendStake/{u}/{habit}", new { friendUserIds = friends });

  [Fact]
  public async Task Set_replaces_and_get_lists_the_watchers()
  {
    var a = await app.NewUser();
    var b = await app.NewUser("bob");
    var c = await app.NewUser("cat");
    await Befriend(a, b);
    await Befriend(a, c);
    var habit = FakeHabit.Add(a);
    var client = As(a);

    var set = await Put(client, a, habit, b, c, b);
    set.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await set.Content.ReadFromJsonAsync<JsonElement>();
    body.GetProperty("habitId").GetString().Should().Be(habit.ToString());
    body.GetProperty("friends").GetArrayLength().Should().Be(2, "duplicates collapse");

    (await Put(client, a, habit, c)).StatusCode.Should().Be(HttpStatusCode.OK);
    var got = await client.GetFromJsonAsync<JsonElement>($"/api/v1/FriendStake/{a}/{habit}");
    got.GetProperty("friends").EnumerateArray().Select(x => x.GetProperty("userId").GetString()).Should().Equal(c);

    (await Put(client, a, habit)).StatusCode.Should().Be(HttpStatusCode.OK);
    (await client.GetFromJsonAsync<JsonElement>($"/api/v1/FriendStake/{a}/{habit}")).GetProperty("friends").GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task Only_friends_and_at_most_twenty()
  {
    var a = await app.NewUser();
    var stranger = await app.NewUser();
    var habit = FakeHabit.Add(a);
    (await Put(As(a), a, habit, stranger)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await Put(As(a), a, habit, [.. Enumerable.Range(0, 21).Select(i => "u" + i)])).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await As(a).PutAsJsonAsync($"/api/v1/FriendStake/{a}/{habit}", new { })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task Removing_a_friend_removes_them_from_the_watchers()
  {
    var a = await app.NewUser();
    var b = await app.NewUser();
    await Befriend(a, b);
    var habit = FakeHabit.Add(a);
    await Put(As(a), a, habit, b);
    await app.ClientAs(b).DeleteAsync($"/api/v1/Friend/{b}/{a}");
    (await app.Count("""SELECT count(*) FROM friend."HabitFriendWatchers" WHERE "HabitId" = @h""", ("h", habit))).Should().Be(0);
  }

  [Fact]
  public async Task A_missed_check_tells_each_watcher_once()
  {
    var a = await app.NewUser("runner");
    var b = await app.NewUser();
    var c = await app.NewUser();
    var d = await app.NewUser();
    await Befriend(a, b);
    await Befriend(a, c);
    await Befriend(a, d);
    var habit = FakeHabit.Add(a, "Run 5k");
    await Put(As(a), a, habit, b, c);
    await Dispatch(); // the friend-accepted notices

    var settledId = Guid.NewGuid();
    await Settle(a, habit, settledId, "missed");
    await Dispatch();
    (await Unread(b)).Should().Be(2, "friend_accepted + friend_missed");
    (await Unread(c)).Should().Be(2);
    (await Unread(d)).Should().Be(1, "d does not watch the habit");

    await Settle(a, habit, settledId, "missed");
    await Dispatch();
    (await Unread(b)).Should().Be(2, "the same check is told once");
  }

  [Fact]
  public async Task Only_a_missed_check_tells_anyone()
  {
    var a = await app.NewUser();
    var b = await app.NewUser();
    await Befriend(a, b);
    var habit = FakeHabit.Add(a);
    await Put(As(a), a, habit, b);
    await Dispatch();
    var before = await Unread(b);
    foreach (var status in new[] { "completed", "skipped", "frozen", "vacation" })
      await Settle(a, habit, Guid.NewGuid(), status);
    await Dispatch();
    (await Unread(b)).Should().Be(before);
  }

  [Fact]
  public async Task A_habit_deleted_event_drops_its_watchers()
  {
    var a = await app.NewUser();
    var b = await app.NewUser();
    await Befriend(a, b);
    var habit = FakeHabit.Add(a);
    await Put(As(a), a, habit, b);
    await using (var scope = host.Services.CreateAsyncScope())
      await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Start(
        () => scope.ServiceProvider.GetRequiredService<IOutbox>().Add("habit.deleted", new { userId = a, habitIds = new[] { habit } }));
    await Dispatch();
    (await app.Count("""SELECT count(*) FROM friend."HabitFriendWatchers" WHERE "HabitId" = @h""", ("h", habit))).Should().Be(0);
  }

  // Check is another lane: stand in for it by emitting its event the way it will.
  private async Task Settle(string userId, Guid habitId, Guid executionId, string status)
  {
    await using var scope = host.Services.CreateAsyncScope();
    var sp = scope.ServiceProvider;
    await sp.GetRequiredService<IUnitOfWork>().Start(() => sp.GetRequiredService<IOutbox>().Add("check.settled",
      new { userId, habitId, executionId, date = "2026-11-20", status, stakeCents = 0, ratioBps = 0, currency = "USD", charityId = (Guid?)null, maxStreak = 0 }));
  }

  private static Task<int> Unread(string userId) => Task.FromResult(FakeNotification.Of(userId).Count);
}
