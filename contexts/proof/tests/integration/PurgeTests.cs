using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ctx.Proof.Domain;
using LazyTax.Platform.Lifecycle;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Proof.Tests.Integration;

public sealed class PurgeTests(KernelApp app)
{
  private readonly ProofHost host = ProofHost.On(app);

  [Fact]
  public async Task Deleting_a_user_leaves_none_of_their_rows_and_keeps_everyone_elses()
  {
    var u = TestTokens.NewUser();
    var other = TestTokens.NewUser();
    var c = this.host.As(u);
    this.host.Plan.Pro.Add(u);
    var h = this.host.Habits.Add(u);
    var ho = this.host.Habits.Add(other);
    const string Athlete = "athlete-";
    var athleteId = 9_000_000 + Random.Shared.Next(900_000);

    // a Strava link, a binding, a tag and a queued event for u; a tag for the other user
    var url = (await (await c.GetAsync($"/api/v1/Strava/connect-url/{u}")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
    var state = Uri.EscapeDataString(System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["state"]!);
    (await this.host.NoRedirect().GetAsync($"/api/v1/Strava/callback?code={Athlete}{athleteId}&scope=activity:read_all&state={state}")).StatusCode.Should().Be(HttpStatusCode.Redirect);
    (await c.PutAsJsonAsync($"/api/v1/Proof/{u}/{h}", new { pluginKey = "strava", settings = """{"sportTypes":["Run"]}""" })).EnsureSuccessStatusCode();
    (await c.PutAsJsonAsync($"/api/v1/NfcTag/{u}/tag-{Guid.NewGuid():N}", new { habitId = h })).EnsureSuccessStatusCode();
    (await this.host.As(other).PutAsJsonAsync($"/api/v1/NfcTag/{other}/tag-{Guid.NewGuid():N}", new { habitId = ho })).EnsureSuccessStatusCode();
    await this.host.InScope(sp => sp.GetRequiredService<IStravaInbox>().Enqueue(new StravaWebhookEvent("activity", "create", 1, athleteId, DateTime.UtcNow, false), default));

    foreach (var table in new[] { "ProofBindings", "NfcTags", "StravaConnections" })
      (await app.Count($"SELECT count(*) FROM proof.\"{table}\" WHERE \"UserId\" = @u", ("u", u))).Should().BeGreaterThan(0, table);

    await this.host.InScope(async sp =>
    {
      foreach (var purge in sp.GetServices<IUserPurge>()) await purge.Purge(u, default);
    });

    foreach (var table in new[] { "ProofBindings", "NfcTags", "StravaConnections" })
      (await app.Count($"SELECT count(*) FROM proof.\"{table}\" WHERE \"UserId\" = @u", ("u", u))).Should().Be(0, table);
    (await app.Count("SELECT count(*) FROM proof.\"StravaEvents\" WHERE \"OwnerId\" = @a", ("a", (long)athleteId))).Should().Be(0);
    (await app.Count("SELECT count(*) FROM proof.\"NfcTags\" WHERE \"UserId\" = @u", ("u", other))).Should().Be(1);
  }

  [Fact]
  public async Task Before_purge_revokes_lazytax_at_strava_and_never_throws()
  {
    var u = TestTokens.NewUser();
    var url = (await (await this.host.As(u).GetAsync($"/api/v1/Strava/connect-url/{u}")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
    var state = Uri.EscapeDataString(System.Web.HttpUtility.ParseQueryString(new Uri(url).Query)["state"]!);
    await this.host.NoRedirect().GetAsync($"/api/v1/Strava/callback?code=athlete-{8_000_000 + Random.Shared.Next(900_000)}&scope=activity:read_all&state={state}");
    var fake = this.host.Factory.Services.GetRequiredService<Ctx.Proof.Gateways.FakeStravaApi>();
    var before = fake.RevokedTokens.Count;
    await this.host.InScope(async sp =>
    {
      foreach (var hook in sp.GetServices<IBeforePurge>()) await hook.Run(u, default);
    });
    fake.RevokedTokens.Count.Should().Be(before + 1);
    await this.host.InScope(async sp =>
    {
      foreach (var hook in sp.GetServices<IBeforePurge>()) await hook.Run(TestTokens.NewUser(), default); // no connection: nothing to do
    });
  }
}
