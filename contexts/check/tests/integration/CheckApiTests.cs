using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ctx.Check.Contract.V1;
using Ctx.Check.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Lifecycle;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Check.Tests.Integration;

public sealed class CheckApiTests(CheckApp app)
{
  private static string Complete(string user, Guid version) => $"/api/v1/Habit/{user}/{version}/executions";

  private static string Skip(string user, Guid version) => $"/api/v1/Habit/{user}/{version}/executions/skip";

  private Task<long> Rows(string table, Guid habitId) =>
    app.Count($"SELECT count(*) FROM \"check\".\"{table}\" WHERE \"HabitId\" = @h", ("h", habitId));

  [Fact]
  public async Task Complete_records_a_succeeded_execution_once()
  {
    var user = TestTokens.NewUser();
    var habit = app.World.Habits.Add(user);
    var client = app.ClientAs(user);

    var res = await client.PostAsJsonAsync(Complete(user, habit.VersionId), new { notes = "done" });
    res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
    var body = await res.Content.ReadFromJsonAsync<JsonElement>();
    body.GetProperty("status").GetString().Should().Be("succeeded");
    body.GetProperty("habitVersionId").GetGuid().Should().Be(habit.VersionId);
    body.GetProperty("notes").GetString().Should().Be("done");

    var again = await client.PostAsJsonAsync(Complete(user, habit.VersionId), new { notes = (string?)null });
    again.StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await this.Rows("HabitExecutions", habit.HabitId)).Should().Be(1);
  }

  [Fact]
  public async Task Complete_needs_a_token_and_the_callers_own_path()
  {
    var a = TestTokens.NewUser();
    var habit = app.World.Habits.Add(a);
    (await app.Anonymous().PostAsJsonAsync(Complete(a, habit.VersionId), new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await app.ClientAs(TestTokens.NewUser()).PostAsJsonAsync(Complete(a, habit.VersionId), new { }))
      .StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public Task Other_user_gets_404_completing_my_habit_version() => OwnerIsolation.AssertOtherUserGets404(app,
    createAsA: (_, a) => Task.FromResult(app.World.Habits.Add(a).VersionId.ToString()),
    requestAsB: (b, id) => new HttpRequestMessage(HttpMethod.Post, Complete(b, Guid.Parse(id))) { Content = JsonContent.Create(new { }) });

  [Fact]
  public Task Other_user_gets_404_on_my_execution() => OwnerIsolation.AssertOtherUserGets404(app,
    createAsA: async (client, a) =>
    {
      var h = app.World.Habits.Add(a);
      var res = await client.PostAsJsonAsync(Complete(a, h.VersionId), new { });
      return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    },
    requestAsB: (b, id) => new HttpRequestMessage(HttpMethod.Get, $"/api/v1/Habit/{b}/executions/{id}"));

  // The owner's bug (LazyTax Domain/Habit/Service.cs:93-97): SkipHabit loaded the version by id only.
  [Fact]
  public async Task Skip_of_another_users_version_is_404_and_changes_nothing()
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    var habit = app.World.Habits.Add(a);
    app.World.Habits.Add(b); // B has habits too

    var res = await app.ClientAs(b).PostAsJsonAsync(Skip(b, habit.VersionId), new { notes = "not mine" });

    res.StatusCode.Should().Be(HttpStatusCode.NotFound, await res.Content.ReadAsStringAsync());
    (await this.Rows("HabitChecks", habit.HabitId)).Should().Be(0);
    (await this.Rows("HabitExecutions", habit.HabitId)).Should().Be(0);
    (await app.Count("SELECT count(*) FROM \"check\".\"HabitExecutions\" WHERE \"UserId\" = @u", ("u", a))).Should().Be(0);
  }

  [Fact]
  public async Task Skip_counts_against_the_months_skips()
  {
    var user = TestTokens.NewUser();
    app.World.Plan.Skips[user] = 1;
    var first = app.World.Habits.Add(user);
    var second = app.World.Habits.Add(user);
    var client = app.ClientAs(user);

    var ok = await client.PostAsJsonAsync(Skip(user, first.VersionId), new { notes = "tired" });
    ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
    (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("skip");

    var over = await client.PostAsJsonAsync(Skip(user, second.VersionId), new { notes = "again" });
    over.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await over.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("type").GetString().Should().Be("/problems/v1/tier_insufficient");
    (await this.Rows("HabitExecutions", second.HabitId)).Should().Be(0);
  }

  [Fact]
  public async Task Skip_with_no_skips_in_the_plan_stops_at_the_guard()
  {
    var user = TestTokens.NewUser();
    app.World.Plan.Skips[user] = 0;
    var habit = app.World.Habits.Add(user);
    var res = await app.ClientAs(user).PostAsJsonAsync(Skip(user, habit.VersionId), new { });
    res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await this.Rows("HabitChecks", habit.HabitId)).Should().Be(0);
  }

  [Fact]
  public async Task Executions_search_filters_by_date_and_validates_it()
  {
    var user = TestTokens.NewUser();
    var habit = app.World.Habits.Add(user);
    var client = app.ClientAs(user);
    await client.PostAsJsonAsync(Complete(user, habit.VersionId), new { });
    var today = DateTime.UtcNow.ToString("dd-MM-yyyy");

    var list = await client.GetFromJsonAsync<JsonElement>($"/api/v1/Habit/{user}/executions?date={today}");
    list.GetArrayLength().Should().Be(1);
    (await client.GetFromJsonAsync<JsonElement>($"/api/v1/Habit/{user}/executions?date=01-01-2001")).GetArrayLength().Should().Be(0);
    (await client.GetAsync($"/api/v1/Habit/{user}/executions?date=2001-01-01")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await client.GetAsync($"/api/v1/Habit/{user}/executions?limit=101")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    // Another user sees none of them.
    var other = TestTokens.NewUser();
    (await app.ClientAs(other).GetFromJsonAsync<JsonElement>($"/api/v1/Habit/{other}/executions")).GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task Overview_shows_streak_week_and_skips()
  {
    var user = TestTokens.NewUser();
    var habit = app.World.Habits.Add(user, h => h.Task = "Read");
    var client = app.ClientAs(user);
    await client.PostAsJsonAsync(Complete(user, habit.VersionId), new { });

    var res = await client.GetFromJsonAsync<JsonElement>($"/api/v1/Habit/{user}/overview");
    res.GetProperty("totalSkip").GetInt32().Should().Be(3);
    res.GetProperty("usedSkip").GetInt32().Should().Be(0);
    res.GetProperty("totalDebt").GetString().Should().Be("0.00");
    var item = res.GetProperty("habits")[0];
    item.GetProperty("name").GetString().Should().Be("Read");
    item.GetProperty("proof").GetString().Should().Be("manual");
    item.GetProperty("stake").GetProperty("amount").GetDecimal().Should().Be(10m);
    item.GetProperty("charity").GetProperty("name").GetString().Should().StartWith("Charity ");
    var status = item.GetProperty("status");
    status.GetProperty("currentStreak").GetInt32().Should().Be(1);
    status.GetProperty("isCompleteToday").GetBoolean().Should().BeTrue();
    status.GetProperty("todayStatus").GetString().Should().Be("succeeded");
  }

  [Fact]
  public async Task Cron_is_admin_only()
  {
    var user = TestTokens.NewUser();
    (await app.ClientAs(user).PostAsync("/api/v1/habit-cron/mark-daily-failures", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await app.Anonymous().PostAsync("/api/v1/habit-cron/mark-daily-failures", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    var ok = await app.ClientAs(user, "admin").PostAsync("/api/v1/habit-cron/mark-daily-failures", null);
    ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task Purge_leaves_no_rows_of_the_user()
  {
    var user = TestTokens.NewUser();
    var habit = app.World.Habits.Add(user);
    await app.ClientAs(user).PostAsJsonAsync(Complete(user, habit.VersionId), new { });
    (await app.Count("SELECT count(*) FROM \"check\".\"HabitEvidence\" WHERE \"UserId\" = @u", ("u", user))).Should().Be(1);

    await app.InScope(async sp =>
    {
      var purge = sp.GetServices<IUserPurge>().OfType<CheckPurge>().Single();
      await sp.GetRequiredService<IUnitOfWork>().Start(() => purge.Purge(user, CancellationToken.None));
    });

    foreach (var t in new[] { "HabitChecks", "HabitEvidence", "HabitExecutions" })
      (await app.Count($"SELECT count(*) FROM \"check\".\"{t}\" WHERE \"UserId\" = @u", ("u", user))).Should().Be(0, t);
  }

  [Fact]
  public async Task Mcp_complete_runs_the_same_rules()
  {
    var a = TestTokens.NewUser();
    var habit = app.World.Habits.Add(a);
    var call = new
    {
      jsonrpc = "2.0",
      id = 1,
      method = "tools/call",
      @params = new { name = "check.complete", arguments = new { userId = a, habitVersionId = habit.VersionId } },
    };
    var asOther = await (await app.ClientAs(TestTokens.NewUser()).PostAsJsonAsync("/mcp", call)).Content.ReadAsStringAsync();
    asOther.Should().Contain("\"isError\":true").And.Contain("\"status\":403");
    var ok = await (await app.ClientAs(a).PostAsJsonAsync("/mcp", call)).Content.ReadAsStringAsync();
    ok.Should().Contain("succeeded");
    (await this.Rows("HabitExecutions", habit.HabitId)).Should().Be(1);
  }
}
