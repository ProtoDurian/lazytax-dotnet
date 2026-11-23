#pragma warning disable xUnit1051 // tests run to completion; no cancellation token needed
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ctx.Habit.Data;
using LazyTax.Platform.Lifecycle;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Habit.Tests.Integration;

public sealed class HabitApiTests(KernelApp app)
{
  private static object Body(string task = "Run", object? extra = null) => new
  {
    task,
    daysOfWeek = new[] { "monday", "Tuesday" },
    notificationTime = "07:30:00",
    stake = "0",
    timezone = "Asia/Singapore",
  };

  private static async Task<JsonElement> Json(HttpResponseMessage res) => await res.Content.ReadFromJsonAsync<JsonElement>();

  private static async Task<string> CreateHabit(HttpClient client, string user, string task = "Run")
  {
    var res = await client.PostAsJsonAsync($"/api/v1/Habit/{user}", Body(task));
    res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
    return (await Json(res)).GetProperty("habitId").GetString()!;
  }

  [Fact]
  public async Task Create_get_search_update_delete()
  {
    var user = TestTokens.NewUser();
    var client = app.ClientAs(user);

    var created = await Json(await client.PostAsJsonAsync($"/api/v1/Habit/{user}", Body()));
    created.GetProperty("version").GetInt32().Should().Be(1);
    created.GetProperty("stake").GetString().Should().Be("0.00");
    created.GetProperty("ratio").GetString().Should().Be("100.0");
    created.GetProperty("deadlineTime").GetString().Should().Be("23:59:00");
    created.GetProperty("daysOfWeek").EnumerateArray().Select(d => d.GetString()).Should().Equal("Monday", "Tuesday");
    var id = created.GetProperty("habitId").GetString()!;

    (await Json(await client.GetAsync($"/api/v1/Habit/{user}/{id}"))).GetProperty("task").GetString().Should().Be("Run");
    (await Json(await client.GetAsync($"/api/v1/Habit/{user}?task=ru"))).GetArrayLength().Should().Be(1);

    var put = await client.PutAsJsonAsync($"/api/v1/Habit/{user}/{id}", new
    {
      task = "Run far",
      daysOfWeek = new[] { "Friday" },
      notificationTime = "06:00:00",
      stake = "0",
      enabled = true,
      timezone = "Asia/Singapore",
    });
    put.StatusCode.Should().Be(HttpStatusCode.OK);
    (await Json(put)).GetProperty("version").GetInt32().Should().Be(2);

    (await client.DeleteAsync($"/api/v1/Habit/{user}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await client.GetAsync($"/api/v1/Habit/{user}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await client.DeleteAsync($"/api/v1/Habit/{user}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task Invalid_body_is_400()
  {
    var user = TestTokens.NewUser();
    var res = await app.ClientAs(user).PostAsJsonAsync($"/api/v1/Habit/{user}", new
    {
      task = "",
      daysOfWeek = new[] { "Someday" },
      notificationTime = "x",
      stake = "-1",
      timezone = "Nope",
    });
    res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task Free_tier_stops_at_its_habit_quota()
  {
    var user = TestTokens.NewUser();
    var client = app.ClientAs(user);
    await CreateHabit(client, user, "one");
    await CreateHabit(client, user, "two");
    var third = await client.PostAsJsonAsync($"/api/v1/Habit/{user}", Body("three"));
    third.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await third.Content.ReadAsStringAsync()).Should().Contain("tier_insufficient");
  }

  [Fact]
  public async Task Concurrent_creates_cannot_pass_the_quota_together()
  {
    var user = TestTokens.NewUser();
    var client = app.ClientAs(user);
    var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => client.PostAsJsonAsync($"/api/v1/Habit/{user}", Body($"h{i}"))));
    results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().BeLessThanOrEqualTo(2);
    (await app.Count("SELECT count(*) FROM habit.\"Habits\" WHERE \"UserId\" = @u", ("u", user))).Should().BeLessThanOrEqualTo(2);
  }

  [Fact]
  public async Task Reorder_puts_requested_first()
  {
    var user = TestTokens.NewUser();
    var client = app.ClientAs(user);
    var a = await CreateHabit(client, user, "a");
    var b = await CreateHabit(client, user, "b");
    var res = await client.PutAsJsonAsync($"/api/v1/Habit/{user}/order", new { habitIds = new[] { b } });
    res.StatusCode.Should().Be(HttpStatusCode.OK);
    (await Json(res)).GetProperty("habitIds").EnumerateArray().Select(x => x.GetString()).Should().Equal(b, a);
    var list = await Json(await client.GetAsync($"/api/v1/Habit/{user}"));
    list.EnumerateArray().Select(x => x.GetProperty("habitId").GetString()).Should().Equal(b, a);
  }

  [Fact]
  public async Task Reorder_with_another_users_habit_is_400()
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    var idA = await CreateHabit(app.ClientAs(a), a);
    var res = await app.ClientAs(b).PutAsJsonAsync($"/api/v1/Habit/{b}/order", new { habitIds = new[] { idA } });
    res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task No_token_is_401()
  {
    var res = await app.Anonymous().GetAsync($"/api/v1/Habit/{TestTokens.NewUser()}");
    res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task Other_users_path_is_403_and_admin_may()
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    (await app.ClientAs(b).GetAsync($"/api/v1/Habit/{a}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await app.ClientAs(b, "admin").GetAsync($"/api/v1/Habit/{a}")).StatusCode.Should().Be(HttpStatusCode.OK);
  }

  // Layer 4: one owner-scoped test per entity route.
  [Fact]
  public Task Get_of_other_users_habit_is_404() => OwnerIsolation.AssertOtherUserGets404(app,
    (client, a) => CreateHabit(client, a),
    (b, id) => new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Habit/{b}/{id}"));

  [Fact]
  public Task Update_of_other_users_habit_is_404() => OwnerIsolation.AssertOtherUserGets404(app,
    (client, a) => CreateHabit(client, a),
    (b, id) => new HttpRequestMessage(HttpMethod.Put, $"/api/v1/Habit/{b}/{id}")
    {
      Content = JsonContent.Create(new { task = "x", daysOfWeek = new[] { "Monday" }, notificationTime = "07:00:00", stake = "0", enabled = true, timezone = "Asia/Singapore" }),
    });

  [Fact]
  public Task Delete_of_other_users_habit_is_404() => OwnerIsolation.AssertOtherUserGets404(app,
    (client, a) => CreateHabit(client, a),
    (b, id) => new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/Habit/{b}/{id}"));

  [Fact]
  public async Task Other_users_delete_leaves_the_habit_alive()
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    var id = await CreateHabit(app.ClientAs(a), a);
    await app.ClientAs(b).DeleteAsync($"/api/v1/Habit/{b}/{id}");
    (await app.ClientAs(a).GetAsync($"/api/v1/Habit/{a}/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public Task Get_of_other_users_routine_is_404() => OwnerIsolation.AssertOtherUserGets404(app,
    async (client, a) => (await Json(await client.PostAsJsonAsync($"/api/v1/Routine/{a}", new { name = "Morning" }))).GetProperty("id").GetString()!,
    (b, id) => new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Routine/{b}/{id}"));

  [Fact]
  public Task Delete_of_other_users_routine_is_404() => OwnerIsolation.AssertOtherUserGets404(app,
    async (client, a) => (await Json(await client.PostAsJsonAsync($"/api/v1/Routine/{a}", new { name = "Morning" }))).GetProperty("id").GetString()!,
    (b, id) => new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/Routine/{b}/{id}"));

  [Fact]
  public async Task Creating_in_another_users_routine_is_404()
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    var routine = (await Json(await app.ClientAs(a).PostAsJsonAsync($"/api/v1/Routine/{a}", new { name = "Morning" }))).GetProperty("id").GetString();
    var res = await app.ClientAs(b).PostAsJsonAsync($"/api/v1/Habit/{b}", new
    {
      task = "x",
      daysOfWeek = new[] { "Monday" },
      notificationTime = "07:00:00",
      stake = "0",
      timezone = "Asia/Singapore",
      routineId = routine,
    });
    res.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task Deleting_a_routine_deletes_its_habits_and_tells_the_outbox()
  {
    var user = TestTokens.NewUser();
    var client = app.ClientAs(user);
    var routine = (await Json(await client.PostAsJsonAsync($"/api/v1/Routine/{user}", new { name = "Morning" }))).GetProperty("id").GetString();
    var res = await client.PostAsJsonAsync($"/api/v1/Habit/{user}", new
    {
      task = "x",
      daysOfWeek = new[] { "Monday" },
      notificationTime = "07:00:00",
      stake = "0",
      timezone = "Asia/Singapore",
      routineId = routine,
    });
    var habit = (await Json(res)).GetProperty("habitId").GetString();

    (await client.DeleteAsync($"/api/v1/Routine/{user}/{routine}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await client.GetAsync($"/api/v1/Habit/{user}/{habit}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await app.Count("SELECT count(*) FROM kernel.outbox_events WHERE name = 'habit.deleted' AND payload->>'userId' = @u", ("u", user)))
      .Should().Be(1);
  }

  [Fact]
  public async Task Create_emits_habit_changed_in_the_same_transaction()
  {
    var user = TestTokens.NewUser();
    await CreateHabit(app.ClientAs(user), user);
    (await app.Count("SELECT count(*) FROM kernel.outbox_events WHERE name = 'habit.changed' AND payload->>'userId' = @u", ("u", user)))
      .Should().Be(1);
  }

  [Fact]
  public async Task Deleting_the_last_habit_deletes_its_one_habit_routine()
  {
    var user = TestTokens.NewUser();
    var client = app.ClientAs(user);
    var id = await CreateHabit(client, user);
    (await Json(await client.GetAsync($"/api/v1/Routine/{user}"))).GetArrayLength().Should().Be(1);
    await client.DeleteAsync($"/api/v1/Habit/{user}/{id}");
    (await Json(await client.GetAsync($"/api/v1/Routine/{user}"))).GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task Purge_removes_every_row_of_the_user()
  {
    var user = TestTokens.NewUser();
    var other = TestTokens.NewUser();
    await CreateHabit(app.ClientAs(user), user);
    await CreateHabit(app.ClientAs(other), other);

    await app.InScope(async sp =>
    {
      var purge = sp.GetServices<IUserPurge>().OfType<HabitPurge>().Single();
      await purge.Purge(user, default);
    });

    foreach (var t in new[] { "Habits", "Routines" })
      (await app.Count($"SELECT count(*) FROM habit.\"{t}\" WHERE \"UserId\" = @u", ("u", user))).Should().Be(0, t);
    (await app.Count("SELECT count(*) FROM habit.\"HabitVersions\" v JOIN habit.\"Habits\" h ON h.\"Id\" = v.\"HabitId\" WHERE h.\"UserId\" = @u", ("u", user))).Should().Be(0);
    (await app.Count("SELECT count(*) FROM habit.\"HabitVersions\" WHERE \"HabitId\" NOT IN (SELECT \"Id\" FROM habit.\"Habits\")")).Should().Be(0);
    (await app.Count("SELECT count(*) FROM habit.\"Habits\" WHERE \"UserId\" = @u", ("u", other))).Should().Be(1);
  }

  [Fact]
  public async Task Mcp_lists_the_habit_tools()
  {
    var user = TestTokens.NewUser();
    var res = await app.ClientAs(user).PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" });
    res.StatusCode.Should().Be(HttpStatusCode.OK);
    (await res.Content.ReadAsStringAsync()).Should().Contain("habit.create").And.Contain("habit.list");
  }
}
