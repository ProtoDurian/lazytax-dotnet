using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LazyTax.Testing;

namespace Ctx.Protection.Tests.Integration;

public sealed class ProtectionApiTests(KernelApp root)
{
  private readonly ProtectionApp app = new(root);

  [Fact]
  public async Task Balance_without_token_is_401()
  {
    var res = await this.app.Factory.CreateClient().GetAsync($"/api/v1/Protection/{TestTokens.NewUser()}");
    res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task Balance_of_another_users_path_is_403()
  {
    var (a, b) = (TestTokens.NewUser(), TestTokens.NewUser());
    var res = await this.app.ClientAs(b).GetAsync($"/api/v1/Protection/{a}");
    res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task A_new_user_has_a_balance_of_zero_and_the_plan_cap()
  {
    var u = TestTokens.NewUser();
    var res = await this.app.ClientAs(u).GetAsync($"/api/v1/Protection/{u}");
    res.StatusCode.Should().Be(HttpStatusCode.OK);
    var j = await res.Content.ReadFromJsonAsync<JsonElement>();
    j.GetProperty("userId").GetString().Should().Be(u);
    j.GetProperty("balance").GetInt32().Should().Be(0);
    j.GetProperty("cap").GetInt32().Should().Be(this.app.Cap);
  }

  [Fact]
  public async Task Reading_the_balance_cuts_it_to_the_cap()
  {
    var u = TestTokens.NewUser();
    await this.app.Seed(u, 9);
    var j = await (await this.app.ClientAs(u).GetAsync($"/api/v1/Protection/{u}")).Content.ReadFromJsonAsync<JsonElement>();
    j.GetProperty("balance").GetInt32().Should().Be(this.app.Cap);
  }

  /// <summary>
  /// The entity here is the user's own protection row. It has no id in any path, so "user B asks for A's row" is
  /// "B reads their own balance and sees none of A's; A's row is unchanged".
  /// </summary>
  [Fact]
  public async Task User_B_never_sees_or_changes_user_As_balance()
  {
    var (a, b) = (TestTokens.NewUser(), TestTokens.NewUser());
    await this.app.Seed(a, 2);
    var res = await this.app.ClientAs(b).GetAsync($"/api/v1/Protection/{b}");
    var j = await res.Content.ReadFromJsonAsync<JsonElement>();
    j.GetProperty("userId").GetString().Should().Be(b);
    j.GetProperty("balance").GetInt32().Should().Be(0);
    (await this.app.BalanceOf(a)).Balance.Should().Be(2);
    (await this.app.Count("UserProtections", a)).Should().Be(1);
  }

  [Fact]
  public async Task Award_weekly_needs_a_token_and_admin()
  {
    var u = TestTokens.NewUser();
    var body = JsonContent.Create(new { userId = u });
    (await this.app.Factory.CreateClient().PostAsync("/api/v1/Protection/award-weekly", body)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await this.app.ClientAs(u).PostAsync("/api/v1/Protection/award-weekly", JsonContent.Create(new { userId = u }))).StatusCode
      .Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task Admin_award_weekly_gives_one_freeze_per_perfect_habit_once()
  {
    var u = TestTokens.NewUser();
    var (h1, h2) = (Guid.NewGuid(), Guid.NewGuid());
    foreach (var day in new[] { "2026-11-16", "2026-11-17" })
    {
      await this.app.Settled(u, h1, day, "done");
      await this.app.Settled(u, h2, day, "done");
    }
    await this.app.Settled(u, Guid.NewGuid(), "2026-11-16", "missed"); // a spoiled habit

    var admin = this.app.ClientAs("admin-1", "admin");
    var first = await (await admin.PostAsJsonAsync("/api/v1/Protection/award-weekly", new { userId = u })).Content.ReadFromJsonAsync<JsonElement>();
    first.GetProperty("awards").GetInt32().Should().Be(2);
    var again = await (await admin.PostAsJsonAsync("/api/v1/Protection/award-weekly", new { userId = u })).Content.ReadFromJsonAsync<JsonElement>();
    again.GetProperty("awards").GetInt32().Should().Be(0);

    (await this.app.BalanceOf(u)).Balance.Should().Be(2);
    (await this.app.Root.Count("SELECT count(*) FROM kernel.outbox_events WHERE name = 'protection.awarded' AND payload->>'userId' = @u", ("u", u)))
      .Should().Be(2);
  }
}
