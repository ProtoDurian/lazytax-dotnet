using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LazyTax.Testing;

namespace Ctx.Proof.Tests.Integration;

/// <summary>Proof against the real account, plan, habit and check contexts (no fakes), over HTTP.</summary>
public sealed class RealContextsTests(KernelApp app)
{
  private static async Task<JsonElement> Json(HttpResponseMessage res) => await res.Content.ReadFromJsonAsync<JsonElement>();

  private async Task<(string User, HttpClient Client, string HabitId)> Setup()
  {
    var user = await app.SignUp();
    var client = app.ClientAs(user);
    var res = await client.PostAsJsonAsync($"/api/v1/Habit/{user}", new
    {
      task = "Run",
      daysOfWeek = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" },
      notificationTime = "00:01:00",
      stake = "0",
      timezone = "UTC",
      deadlineTime = "23:59:00",
    });
    res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
    return (user, client, (await Json(res)).GetProperty("habitId").GetString()!);
  }

  [Fact]
  public async Task Nfc_tap_completes_a_real_habit_through_real_check()
  {
    var (user, client, habit) = await Setup();
    var tag = "tag-" + Guid.NewGuid().ToString("N")[..16];
    (await client.PutAsJsonAsync($"/api/v1/NfcTag/{user}/{tag}", new { habitId = habit })).StatusCode.Should().Be(HttpStatusCode.OK);

    var before = await Json(await client.GetAsync($"/api/v1/NfcTag/{user}/{tag}"));
    before.GetProperty("habitVersion").GetProperty("habitId").GetString().Should().Be(habit);
    before.GetProperty("todayExecution").ValueKind.Should().Be(JsonValueKind.Null);

    var tap = await client.PostAsJsonAsync($"/api/v1/NfcTag/{user}/{tag}/tap", new { });
    tap.StatusCode.Should().Be(HttpStatusCode.OK, await tap.Content.ReadAsStringAsync());
    (await Json(tap)).EnumerateArray().Single().GetProperty("executionId").ValueKind.Should().Be(JsonValueKind.String);

    var after = await Json(await client.GetAsync($"/api/v1/NfcTag/{user}/{tag}"));
    after.GetProperty("todayExecution").GetProperty("status").GetString().Should().Be("succeeded");

    // the day is settled: a second tap changes nothing
    (await Json(await client.PostAsJsonAsync($"/api/v1/NfcTag/{user}/{tag}/tap", new { }))).EnumerateArray().Single()
      .GetProperty("executionId").ValueKind.Should().Be(JsonValueKind.Null);
    (await app.Count("SELECT count(*) FROM \"check\".\"HabitExecutions\" WHERE \"HabitId\" = @h", ("h", Guid.Parse(habit)))).Should().Be(1);
  }

  [Fact]
  public async Task Another_users_habit_and_tag_are_404_through_the_real_contexts()
  {
    var (a, ca, habit) = await Setup();
    var tag = "tag-" + Guid.NewGuid().ToString("N")[..16];
    (await ca.PutAsJsonAsync($"/api/v1/NfcTag/{a}/{tag}", new { habitId = habit })).EnsureSuccessStatusCode();

    var b = await app.SignUp();
    var cb = app.ClientAs(b);
    (await cb.GetAsync($"/api/v1/Proof/{b}/{habit}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await cb.PutAsJsonAsync($"/api/v1/NfcTag/{b}/tag-{Guid.NewGuid():N}", new { habitId = habit })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await cb.GetAsync($"/api/v1/NfcTag/{b}/{tag}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await cb.PostAsJsonAsync($"/api/v1/NfcTag/{b}/{tag}/tap", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await app.Count("SELECT count(*) FROM \"check\".\"HabitExecutions\" WHERE \"HabitId\" = @h", ("h", Guid.Parse(habit)))).Should().Be(0);
  }

  [Fact]
  public async Task Deleting_the_habit_releases_its_tags_through_the_real_outbox()
  {
    var (user, client, habit) = await Setup();
    var tag = "tag-" + Guid.NewGuid().ToString("N")[..16];
    (await client.PutAsJsonAsync($"/api/v1/NfcTag/{user}/{tag}", new { habitId = habit })).EnsureSuccessStatusCode();
    (await client.DeleteAsync($"/api/v1/Habit/{user}/{habit}")).StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    await app.DispatchOutbox();
    (await app.Count("SELECT count(*) FROM proof.\"NfcTags\" WHERE \"Id\" = @t", ("t", tag))).Should().Be(0);
  }
}
