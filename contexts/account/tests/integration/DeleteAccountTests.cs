using System.Net;
using System.Net.Http.Json;
using LazyTax.Testing;

namespace Ctx.Account.Tests.Integration;

/// <summary>Delete account: every context's purge hook runs in one unit of work.</summary>
public sealed class DeleteAccountTests(KernelApp app)
{
  [Fact]
  public async Task Delete_me_purges_every_context()
  {
    var user = await app.SignUp();
    await app.SetTier(user, "pro");
    var client = app.ClientAs(user);
    await client.PostAsJsonAsync($"/api/v1/Vacation/{user}", new { startDate = "01-01-2099", endDate = "05-01-2099", timezone = "UTC" })
      .Expect(HttpStatusCode.OK);

    await client.DeleteAsync("/api/v1/User/Me").Expect(HttpStatusCode.NoContent);

    (await app.Count("SELECT count(*) FROM account.\"Users\" WHERE \"Id\" = @u", ("u", user))).Should().Be(0);
    (await app.Count("SELECT count(*) FROM account.\"Configurations\" WHERE \"UserId\" = @u", ("u", user))).Should().Be(0);
    (await app.Count("SELECT count(*) FROM plan.\"UserSubscriptions\" WHERE \"UserId\" = @u", ("u", user))).Should().Be(0);
    (await app.Count("SELECT count(*) FROM vacation.\"VacationPeriods\" WHERE \"UserId\" = @u", ("u", user))).Should().Be(0);
    (await app.Count("SELECT count(*) FROM kernel.outbox_events WHERE name = 'account.user_deleted' AND payload->>'userId' = @u", ("u", user))).Should().Be(1);
  }

  [Fact]
  public async Task Delete_me_twice_is_fine()
  {
    var client = app.ClientAs(await app.SignUp());
    await client.DeleteAsync("/api/v1/User/Me").Expect(HttpStatusCode.NoContent);
    await client.DeleteAsync("/api/v1/User/Me").Expect(HttpStatusCode.NoContent);
  }

  [Fact]
  public async Task Admin_delete_of_a_missing_user_is_404() =>
    await app.ClientAs("root", "admin").DeleteAsync($"/api/v1/User/{TestTokens.NewUser()}").Expect(HttpStatusCode.NotFound);
}
