using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Proof.Tests.Integration;

public sealed class NfcTagApiTests(KernelApp app)
{
  private readonly ProofHost host = ProofHost.On(app);

  private static string Tag() => "tag-" + Guid.NewGuid().ToString("N")[..16];

  [Fact]
  public async Task Link_resolve_tap_unlink()
  {
    var u = TestTokens.NewUser();
    var h = this.host.Habits.Add(u);
    var tag = Tag();
    var c = this.host.As(u);

    var link = await c.PutAsJsonAsync($"/api/v1/NfcTag/{u}/{tag}", new { habitId = h });
    link.StatusCode.Should().Be(HttpStatusCode.OK, await link.Content.ReadAsStringAsync());
    (await link.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("habitId").GetGuid().Should().Be(h);
    // re-link the same tag by the same user is fine
    (await c.PutAsJsonAsync($"/api/v1/NfcTag/{u}/{tag}", new { habitId = h })).StatusCode.Should().Be(HttpStatusCode.OK);

    var resolved = await c.GetFromJsonAsync<JsonElement>($"/api/v1/NfcTag/{u}/{tag}");
    resolved.GetProperty("tag").GetProperty("id").GetString().Should().Be(tag);
    resolved.GetProperty("habitVersion").GetProperty("habitId").GetGuid().Should().Be(h);
    resolved.GetProperty("todayExecution").ValueKind.Should().Be(JsonValueKind.Null);
    (await this.host.As(u).GetFromJsonAsync<JsonElement>($"/api/v1/NfcTag/{u}/{tag}")).GetProperty("today").GetString().Should().MatchRegex(@"^\d{2}-\d{2}-\d{4}$");

    var tap = await c.PostAsJsonAsync($"/api/v1/NfcTag/{u}/{tag}/tap", new { });
    tap.StatusCode.Should().Be(HttpStatusCode.OK, await tap.Content.ReadAsStringAsync());
    (await tap.Content.ReadFromJsonAsync<List<JsonElement>>())!.Should().ContainSingle().Which.GetProperty("executionId").ValueKind.Should().Be(JsonValueKind.String);
    this.host.Check.Reports.Should().ContainSingle(r => r.HabitId == h && r.Evidence.ProofKey == "nfc");

    (await c.GetFromJsonAsync<JsonElement>($"/api/v1/NfcTag/{u}/{tag}")).GetProperty("todayExecution").GetProperty("status").GetString().Should().Be("succeeded");

    (await c.DeleteAsync($"/api/v1/NfcTag/{u}/{tag}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await c.DeleteAsync($"/api/v1/NfcTag/{u}/{tag}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await c.GetAsync($"/api/v1/NfcTag/{u}/{tag}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task First_user_to_claim_a_tag_keeps_it_and_the_second_gets_409_with_nothing_changed()
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    var ha = this.host.Habits.Add(a);
    var hb = this.host.Habits.Add(b);
    var tag = Tag();
    (await this.host.As(a).PutAsJsonAsync($"/api/v1/NfcTag/{a}/{tag}", new { habitId = ha })).StatusCode.Should().Be(HttpStatusCode.OK);
    (await this.host.As(b).PutAsJsonAsync($"/api/v1/NfcTag/{b}/{tag}", new { habitId = hb })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    (await app.Count("SELECT count(*) FROM proof.\"NfcTags\" WHERE \"Id\" = @t AND \"UserId\" = @u AND \"HabitId\" = @h", ("t", tag), ("u", a), ("h", ha))).Should().Be(1);
  }

  [Fact]
  public async Task Two_users_racing_for_one_tag_leave_exactly_one_owner()
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    var ha = this.host.Habits.Add(a);
    var hb = this.host.Habits.Add(b);
    var tag = Tag();
    var results = await Task.WhenAll(
      this.host.As(a).PutAsJsonAsync($"/api/v1/NfcTag/{a}/{tag}", new { habitId = ha }),
      this.host.As(b).PutAsJsonAsync($"/api/v1/NfcTag/{b}/{tag}", new { habitId = hb }));
    results.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
    (await app.Count("SELECT count(*) FROM proof.\"NfcTags\" WHERE \"Id\" = @t", ("t", tag))).Should().Be(1);
  }

  [Fact]
  public async Task Another_users_tag_reads_as_not_found_on_resolve_unlink_and_tap_and_is_never_changed()
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    var ha = this.host.Habits.Add(a);
    this.host.Habits.Add(b);
    var tag = Tag();
    (await this.host.As(a).PutAsJsonAsync($"/api/v1/NfcTag/{a}/{tag}", new { habitId = ha })).EnsureSuccessStatusCode();

    var cb = this.host.As(b);
    (await cb.GetAsync($"/api/v1/NfcTag/{b}/{tag}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await cb.DeleteAsync($"/api/v1/NfcTag/{b}/{tag}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await cb.PostAsJsonAsync($"/api/v1/NfcTag/{b}/{tag}/tap", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await (await cb.GetAsync($"/api/v1/NfcTag/{b}/{tag}")).Content.ReadAsStringAsync()).Should().NotContain(a);
    this.host.Check.Reports.Should().NotContain(r => r.HabitId == ha);
    (await app.Count("SELECT count(*) FROM proof.\"NfcTags\" WHERE \"Id\" = @t AND \"UserId\" = @u", ("t", tag), ("u", a))).Should().Be(1);
  }

  [Fact]
  public async Task Link_needs_an_owned_habit_and_a_valid_tag_id()
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    var hb = this.host.Habits.Add(b);
    var c = this.host.As(a);
    (await c.PutAsJsonAsync($"/api/v1/NfcTag/{a}/{Tag()}", new { habitId = hb })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await c.PutAsJsonAsync($"/api/v1/NfcTag/{a}/{Tag()}", new { habitId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await c.PutAsJsonAsync($"/api/v1/NfcTag/{a}/short", new { habitId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await c.GetAsync($"/api/v1/NfcTag/{a}/bad%20tag%20id%21%21")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await c.PutAsJsonAsync($"/api/v1/NfcTag/{a}/{Tag()}", new { habitId = Guid.Empty })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task Anonymous_is_401_and_another_users_path_is_403()
  {
    var u = TestTokens.NewUser();
    var tag = Tag();
    (await this.host.Anonymous().GetAsync($"/api/v1/NfcTag/{u}/{tag}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await this.host.Anonymous().PostAsJsonAsync($"/api/v1/NfcTag/{u}/{tag}/tap", new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await this.host.As(TestTokens.NewUser()).GetAsync($"/api/v1/NfcTag/{u}/{tag}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await this.host.As(TestTokens.NewUser()).PostAsJsonAsync($"/api/v1/NfcTag/{u}/{tag}/tap", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task A_deleted_habit_releases_its_binding_and_tags_through_the_outbox()
  {
    var u = TestTokens.NewUser();
    var h = this.host.Habits.Add(u);
    var tag = Tag();
    (await this.host.As(u).PutAsJsonAsync($"/api/v1/NfcTag/{u}/{tag}", new { habitId = h })).EnsureSuccessStatusCode();
    await this.host.InScope(sp => sp.GetRequiredService<LazyTax.Platform.Data.IUnitOfWork>().Start(
      () => sp.GetRequiredService<LazyTax.Platform.Outbox.IOutbox>().Add("habit.deleted", new { userId = u, habitIds = new[] { h } })));
    await this.host.App.DispatchOutbox();
    (await app.Count("SELECT count(*) FROM proof.\"NfcTags\" WHERE \"UserId\" = @u", ("u", u))).Should().Be(0);
  }
}
