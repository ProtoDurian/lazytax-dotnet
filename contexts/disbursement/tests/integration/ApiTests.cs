#pragma warning disable xUnit1051 // the host calls take no test-scoped token; tests are short
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ctx.Disbursement.Contract.V1;
using Ctx.Disbursement.Domain;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ctx.Penalty.Contract.V1;

namespace Ctx.Disbursement.Tests.Integration;

/// <summary>
/// Routes under /api/v1/disbursement. Payouts belong to no user, so there is no "other user's row": every route is
/// admin-only and a non-admin gets 403 whatever id they ask for (the nearest equivalent of the owner-scoped 404 test).
/// </summary>
public sealed class ApiTests(KernelApp app)
{
  private const string Base = "/api/v1/disbursement";

  private async Task<Guid> InsertPayout()
  {
    var id = Guid.NewGuid();
    var now = DateTime.UtcNow;
    await app.InScope(sp => sp.GetRequiredService<IDisbursementRepository>().Insert(
      new DisbursementInfo(id, Guid.NewGuid(), "USD", 4200, DisbursementStatus.Completed, "org_x", "don_x", 0, null, now, now)));
    return id;
  }

  [Theory]
  [InlineData("POST", "/run")]
  [InlineData("GET", "")]
  [InlineData("GET", "/00000000-0000-0000-0000-000000000001")]
  public async Task No_token_is_401(string method, string path)
  {
    var res = await app.Anonymous().SendAsync(new HttpRequestMessage(new HttpMethod(method), Base + path));
    res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Theory]
  [InlineData("POST", "/run")]
  [InlineData("GET", "")]
  [InlineData("GET", "/00000000-0000-0000-0000-000000000001")]
  public async Task Non_admin_is_403(string method, string path)
  {
    var res = await app.ClientAs(TestTokens.NewUser()).SendAsync(new HttpRequestMessage(new HttpMethod(method), Base + path));
    res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task Non_admin_cannot_read_an_existing_payout()
  {
    var id = await this.InsertPayout();
    var res = await app.ClientAs(TestTokens.NewUser()).GetAsync($"{Base}/{id}");
    res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await res.Content.ReadAsStringAsync()).Should().NotContain("don_x");
  }

  [Fact]
  public async Task Admin_reads_a_payout_and_an_unknown_id_is_404()
  {
    var id = await this.InsertPayout();
    var admin = app.ClientAs(TestTokens.NewUser(), "admin");

    var ok = await admin.GetAsync($"{Base}/{id}");
    ok.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await ok.Content.ReadFromJsonAsync<JsonElement>();
    body.GetProperty("amountCents").GetInt64().Should().Be(4200);
    body.GetProperty("status").GetString().Should().Be("completed");

    (await admin.GetAsync($"{Base}/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task Admin_lists_filtered_by_status_and_rejects_a_bad_status()
  {
    var id = await this.InsertPayout();
    var admin = app.ClientAs(TestTokens.NewUser(), "admin");

    var list = await admin.GetFromJsonAsync<JsonElement>($"{Base}?status=completed&limit=100");
    list.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).Should().Contain(id);
    (await admin.GetAsync($"{Base}?status=bogus")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task Admin_run_pays_out_and_returns_the_completed_count()
  {
    var penalty = new FakePenalty();
    var targets = new FakeTargets();
    var charity = Guid.NewGuid();
    targets.Add(charity);
    penalty.AddCharged(charity, "USD", 3300);
    await using var host = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
    {
      s.RemoveAll<IPenaltyV1>();
      s.AddSingleton<IPenaltyV1>(penalty);
      s.RemoveAll<IPayoutTargets>();
      s.AddSingleton<IPayoutTargets>(targets);
    }));
    var res = await host.CreateClient().As(TestTokens.NewUser(), "admin").PostAsync($"{Base}/run", null);
    res.StatusCode.Should().Be(HttpStatusCode.OK);
    (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("completed").GetInt32().Should().Be(1);
    var paid = await host.CreateClient().As(TestTokens.NewUser(), "admin").GetFromJsonAsync<JsonElement>($"{Base}?charityId={charity}");
    paid[0].GetProperty("amountCents").GetInt64().Should().Be(3300);
  }
}
