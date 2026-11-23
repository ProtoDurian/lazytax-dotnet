using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ctx.Check.Domain;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Check.Tests.Integration;

/// <summary>The whole app with every real context (no fakes), in its own fresh database.</summary>
public sealed class RealApp : KernelApp;

/// <summary>
/// SIT flow 1 (a missed habit) on the whole app. A black-box run on the real clock can never reach "missed"
/// (a habit made now has its first deadline ahead), and the app has no time override, so this test keeps
/// flow 1's routes and bodies and calls <see cref="CheckService.Tick(DateTime, CancellationToken)"/> with a
/// clock past the deadline. tests/sit-flows/flow1-missed-habit runs the part before the deadline.
/// </summary>
public sealed class MissedHabitFlowTests(RealApp app) : IClassFixture<RealApp>
{
  // payment's dev default (Contexts:payment WebhookSecret); the test app sets no other.
  private const string WebhookSecret = "whsec_lazytax_dev";

  private static async Task<JsonElement> Json(HttpResponseMessage res) => await res.Content.ReadFromJsonAsync<JsonElement>();

  private static readonly string[] AllDays = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

  /// <summary>The user links a card: a payment customer, then Airwallex's signed payment_consent.verified webhook.</summary>
  private async Task Consent(string user)
  {
    var cust = await app.ClientAs(user).PutAsync($"/api/v1/Payment/{user}/customers", null);
    cust.StatusCode.Should().Be(HttpStatusCode.OK, await cust.Content.ReadAsStringAsync());
    var customerId = (await Json(cust)).GetProperty("customerId").GetString()!;
    var body = JsonSerializer.Serialize(new
    {
      id = $"evt_{user}",
      name = "payment_consent.verified",
      data = new { @object = new { id = $"cst_{user}", status = "VERIFIED", customer_id = customerId, merchant_trigger_reason = "unscheduled" } },
    });
    var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
    var sig = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), Encoding.UTF8.GetBytes(ts + body)));
    using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Payment/webhook")
    {
      Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };
    req.Headers.Add("x-timestamp", ts);
    req.Headers.Add("x-signature", sig);
    var res = await app.Anonymous().SendAsync(req);
    ((int)res.StatusCode).Should().BeLessThan(300, await res.Content.ReadAsStringAsync());
  }

  private async Task Drain()
  {
    for (var i = 0; i < 3; i++) await app.DispatchOutbox();
  }

  private async Task<List<(string Status, long Cents)>> Penalties(string user)
  {
    var list = new List<(string, long)>();
    await using var conn = new Npgsql.NpgsqlConnection(app.ConnectionString);
    await conn.OpenAsync();
    await using var cmd = new Npgsql.NpgsqlCommand(
      "SELECT \"Status\", \"AmountCents\" FROM penalty.\"Penalties\" WHERE \"UserId\" = @u", conn);
    cmd.Parameters.AddWithValue("u", user);
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync()) list.Add((r.GetString(0), r.GetInt64(1)));
    return list;
  }

  [Fact]
  public async Task A_missed_habit_fails_its_execution_stakes_a_penalty_then_charges_it_and_posts_an_inbox_item()
  {
    // Arrange: a charity, a signed-up user in UTC with a verified penalty consent, a habit with a 5.00 stake.
    var admin = app.ClientAs(TestTokens.NewUser(), "admin");
    var charity = await admin.PostAsJsonAsync("/api/v1/Charity", new { name = "Flow Charity", countries = new[] { "SG" } });
    charity.StatusCode.Should().Be(HttpStatusCode.OK, await charity.Content.ReadAsStringAsync());
    var charityId = (await Json(charity)).GetProperty("id").GetString();
    var user = await app.SignUp();
    await this.Consent(user);
    var client = app.ClientAs(user);
    var habit = await client.PostAsJsonAsync($"/api/v1/Habit/{user}", new
    {
      task = "flow habit",
      daysOfWeek = AllDays,
      notificationTime = "00:00:00",
      deadlineTime = "23:59:00",
      stake = "5.00",
      charityId,
      timezone = "UTC",
    });
    habit.StatusCode.Should().Be(HttpStatusCode.OK, await habit.Content.ReadAsStringAsync());
    var versionId = (await Json(habit)).GetProperty("id").GetString();
    await this.Drain();
    (await admin.PostAsync("/api/v1/habit-cron/mark-daily-failures", null)).StatusCode.Should().Be(HttpStatusCode.OK); // opens today

    // Act: today ends unproven; the tick at tomorrow 01:00 UTC (deadline + grace passed) settles it.
    var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1).ToDateTime(new TimeOnly(1, 0), DateTimeKind.Utc);
    await app.InScope(sp => sp.GetRequiredService<CheckService>().Tick(tomorrow, CancellationToken.None));
    await this.Drain();

    // Assert: LazyTax's "penalty pending" is a failed execution with paymentProcessed false.
    var rows = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/Habit/{user}/executions")).EnumerateArray()
      .Where(e => e.GetProperty("habitVersionId").GetString() == versionId).ToList();
    rows.Should().ContainSingle();
    rows[0].GetProperty("status").GetString().Should().Be("failed");
    rows[0].GetProperty("paymentProcessed").GetBoolean().Should().BeFalse();
    (await this.Penalties(user)).Should().Equal(("pending", 500L));
    (await client.GetFromJsonAsync<JsonElement>($"/api/v1/Inbox/{user}")).GetArrayLength().Should().Be(0);

    // Act: the penalty processor charges it through the fake Airwallex, outside any transaction.
    (await admin.PostAsync("/api/v1/Penalty/admin/process", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    await this.Drain();

    // Assert: the outcome reaches the user's inbox.
    var inbox = await client.GetFromJsonAsync<JsonElement>($"/api/v1/Inbox/{user}");
    inbox.GetArrayLength().Should().Be(1, inbox.ToString());
    (await this.Penalties(user)).Should().Equal(("charged", 500L));
  }
}
