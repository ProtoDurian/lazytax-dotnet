using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ctx.Penalty.Contract.V1;
using Ctx.Penalty.Domain;
using Ctx.Penalty.Hooks;
using LazyTax.Platform.Context;
using LazyTax.Platform.Data;
using LazyTax.Platform.Lifecycle;
using LazyTax.Platform.Problems;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Penalty.Tests.Integration;

public sealed class PenaltyApiTests(PenaltyApp app)
{
  private const string Base = "/api/v1/Penalty";

  [Fact]
  public async Task Missed_check_shows_up_as_a_pending_penalty_once()
  {
    var u = TestTokens.NewUser();
    var e = PenaltyApp.Missed(u, stakeCents: 1234);
    await app.Settle(e);
    await app.Settle(e); // replayed event: still one row

    var list = await app.ClientAs(u).GetFromJsonAsync<JsonElement>($"{Base}/{u}", Ct);
    list.GetArrayLength().Should().Be(1);
    var p = list[0];
    p.GetProperty("status").GetString().Should().Be(PenaltyStatus.Pending);
    p.GetProperty("amountCents").GetInt64().Should().Be(1234);
    p.GetProperty("habitExecutionId").GetGuid().Should().Be(e.ExecutionId!.Value);

    var one = await app.ClientAs(u).GetFromJsonAsync<JsonElement>($"{Base}/{u}/{p.GetProperty("id").GetGuid()}", Ct);
    one.GetProperty("userId").GetString().Should().Be(u);
  }

  [Fact]
  public async Task Outstanding_route_sums_uncharged_penalties()
  {
    var u = TestTokens.NewUser();
    await app.MissedPenalty(u, 500);
    await app.MissedPenalty(u, 250);
    var d = await app.ClientAs(u).GetFromJsonAsync<JsonElement>($"{Base}/{u}/outstanding", Ct);
    d.GetProperty("totalCents").GetInt64().Should().Be(750);
    d.GetProperty("currency").GetString().Should().Be("USD");
    d.GetProperty("count").GetInt32().Should().Be(2);
  }

  [Fact]
  public async Task Search_filters_by_status_and_rejects_a_bad_one()
  {
    var u = TestTokens.NewUser();
    await app.MissedPenalty(u);
    var c = app.ClientAs(u);
    (await c.GetFromJsonAsync<JsonElement>($"{Base}/{u}?status=charged", Ct)).GetArrayLength().Should().Be(0);
    (await c.GetFromJsonAsync<JsonElement>($"{Base}/{u}?status=pending", Ct)).GetArrayLength().Should().Be(1);
    var bad = await c.GetAsync($"{Base}/{u}?status=paid", Ct);
    bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public Task Other_user_gets_404_on_a_penalty() => OwnerIsolation.AssertOtherUserGets404(app,
    createAsA: async (_, a) => (await app.MissedPenalty(a)).Id.ToString(),
    requestAsB: (b, id) => new HttpRequestMessage(HttpMethod.Get, $"{Base}/{b}/{id}"));

  [Fact]
  public async Task Other_user_lists_none_of_As_penalties()
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    await app.MissedPenalty(a);
    (await app.ClientAs(b).GetFromJsonAsync<JsonElement>($"{Base}/{b}", Ct)).GetArrayLength().Should().Be(0);
    (await app.ClientAs(b).GetFromJsonAsync<JsonElement>($"{Base}/{b}/outstanding", Ct)).GetProperty("totalCents").GetInt64().Should().Be(0);
  }

  [Fact]
  public async Task Unknown_penalty_is_404()
  {
    var u = TestTokens.NewUser();
    var res = await app.ClientAs(u).GetAsync($"{Base}/{u}/{Guid.NewGuid()}", Ct);
    res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await res.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("type").GetString().Should().Be("/problems/v1/entity_not_found");
  }

  [Theory]
  [InlineData("GET", "/u_x")]
  [InlineData("GET", "/u_x/outstanding")]
  [InlineData("GET", "/admin/search")]
  [InlineData("GET", "/admin/charity-balances")]
  [InlineData("POST", "/admin/process")]
  public async Task No_token_is_401(string method, string path)
  {
    var res = await app.Anonymous().SendAsync(new HttpRequestMessage(new HttpMethod(method), Base + path), Ct);
    res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task Another_users_path_is_403()
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    (await app.ClientAs(b).GetAsync($"{Base}/{a}", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await app.ClientAs(b).GetAsync($"{Base}/{a}/outstanding", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await app.ClientAs(b).GetAsync($"{Base}/{a}/{Guid.NewGuid()}", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Theory]
  [InlineData("GET", "/admin/search")]
  [InlineData("GET", "/admin/charity-balances")]
  [InlineData("POST", "/admin/process")]
  public async Task Admin_routes_are_403_for_a_user(string method, string path)
  {
    var res = await app.ClientAs(TestTokens.NewUser()).SendAsync(new HttpRequestMessage(new HttpMethod(method), Base + path), Ct);
    res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public async Task Admin_sees_any_users_penalties_and_a_user_path_too()
  {
    var u = TestTokens.NewUser();
    var p = await app.MissedPenalty(u);
    var admin = app.ClientAs(TestTokens.NewUser(), "admin");
    var all = await admin.GetFromJsonAsync<JsonElement>($"{Base}/admin/search?userId={u}", Ct);
    all.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).Should().Contain(p.Id);
    (await admin.GetAsync($"{Base}/{u}/{p.Id}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public async Task Mcp_outstanding_uses_the_same_rules()
  {
    var u = TestTokens.NewUser();
    await app.MissedPenalty(u, 400);
    var ok = await Mcp(app.ClientAs(u), "penalty.outstanding", new { userId = u });
    ok.GetProperty("result").GetProperty("structuredContent").GetProperty("totalCents").GetInt64().Should().Be(400);

    var other = await Mcp(app.ClientAs(TestTokens.NewUser()), "penalty.outstanding", new { userId = u });
    other.GetProperty("result").GetProperty("structuredContent").GetProperty("status").GetInt32().Should().Be(403);
  }

  private static async Task<JsonElement> Mcp(HttpClient c, string tool, object args)
  {
    var res = await c.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = args } }, Ct);
    return await res.Content.ReadFromJsonAsync<JsonElement>(Ct);
  }

  [Fact]
  public void Subscribes_to_check_settled_and_payment_completed()
  {
    var subs = app.Services.GetRequiredService<IReadOnlyList<SubscriptionDefinition>>();
    subs.Should().Contain(s => s.EventName == "check.settled" && s.Handler == typeof(CheckSettledHandler));
    subs.Should().Contain(s => s.EventName == "payment.completed" && s.Handler == typeof(PaymentCompletedHandler));
  }
}

public sealed class PenaltyDrainTests(PenaltyApp app)
{
  [Fact]
  public async Task Drain_charges_credits_the_charity_and_emits_charged()
  {
    var u = TestTokens.NewUser();
    var charity = Guid.NewGuid();
    var p1 = await app.MissedPenalty(u, 300, charityId: charity);
    var p2 = await app.MissedPenalty(u, 200, charityId: charity);

    var admin = app.ClientAs(TestTokens.NewUser(), "admin");
    var res = await admin.PostAsync("/api/v1/Penalty/admin/process", null, Ct);
    res.StatusCode.Should().Be(HttpStatusCode.OK);
    (await res.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("processed").GetInt32().Should().BeGreaterThanOrEqualTo(2);

    (await app.Read(p1.Id))!.Status.Should().Be(PenaltyStatus.Charged);
    (await app.Read(p2.Id))!.PaymentIntentId.Should().Be("int_" + p2.Id.ToString("N"));
    var balances = await admin.GetFromJsonAsync<JsonElement>("/api/v1/Penalty/admin/charity-balances", Ct);
    balances.EnumerateArray().Single(b => b.GetProperty("charityId").GetGuid() == charity)
      .GetProperty("accruedCents").GetInt64().Should().Be(500);
    (await app.Count("SELECT count(*) FROM kernel.outbox_events WHERE name = 'penalty.charged' AND payload->>'userId' = @u", ("u", u)))
      .Should().Be(2);
    (await app.ClientAs(u).GetFromJsonAsync<JsonElement>($"/api/v1/Penalty/{u}/outstanding", Ct)).GetProperty("totalCents").GetInt64().Should().Be(0);
  }

  [Fact]
  public async Task Drain_skips_without_consent_and_fails_at_the_cap()
  {
    var skip = TestTokens.NewUser();
    var fail = TestTokens.NewUser();
    app.Gateway.ByUser[skip] = _ => new ChargeOutcome.NoConsent();
    app.Gateway.ByUser[fail] = _ => new ChargeOutcome.Error("card declined");
    var ps = await app.MissedPenalty(skip);
    var pf = await app.MissedPenalty(fail);

    for (var i = 0; i < 5; i++) await app.Drain();

    (await app.Read(ps.Id))!.Status.Should().Be(PenaltyStatus.Skipped);
    var f = (await app.Read(pf.Id))!;
    f.Status.Should().Be(PenaltyStatus.Failed);
    f.Attempts.Should().Be(5);
    f.LastError.Should().Be("card declined");
    (await app.Count("SELECT count(*) FROM kernel.outbox_events WHERE name = 'penalty.failed' AND payload->>'userId' = @u", ("u", fail)))
      .Should().Be(1);
    // Skipped and failed are still owed.
    (await app.InScope(sp => sp.GetRequiredService<IPenaltyV1>().Outstanding(skip))).TotalCents.Should().Be(1000);
  }

  [Fact]
  public async Task Late_payment_completed_marks_charged_once()
  {
    var u = TestTokens.NewUser();
    app.Gateway.ByUser[u] = _ => new ChargeOutcome.Pending("int_late", "REQUIRES_CUSTOMER_ACTION");
    var p = await app.MissedPenalty(u, 700);
    await app.Drain();
    (await app.Read(p.Id))!.Should().Match<PenaltyInfo>(x => x.Status == PenaltyStatus.Pending && x.Attempts == 1);

    var completed = new PaymentCompleted(p.Id, "penalty", "int_late");
    for (var i = 0; i < 2; i++)
      await app.InScope(async sp =>
      {
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var h = sp.GetRequiredService<PaymentCompletedHandler>();
        await uow.Start(() => h.Handle(new(Guid.NewGuid(), "payment.completed", DateTime.UtcNow, 0), completed, Ct));
      });

    (await app.Read(p.Id))!.Status.Should().Be(PenaltyStatus.Charged);
    (await app.Count("SELECT count(*) FROM kernel.outbox_events WHERE name = 'penalty.charged' AND payload->>'userId' = @u", ("u", u)))
      .Should().Be(1);
    (await app.Count("SELECT \"AccruedCents\" FROM penalty.\"CharityBalances\" WHERE \"CharityId\" = @c", ("c", p.CharityId)))
      .Should().Be(700);
  }

  [Fact]
  public async Task Payout_claims_locks_and_releases()
  {
    var u = TestTokens.NewUser();
    var charity = Guid.NewGuid();
    await app.MissedPenalty(u, 600, "EUR", charity);
    await app.MissedPenalty(u, 500, "EUR", charity);
    await app.Drain();
    var disbursement = Guid.NewGuid();

    var stamped = await app.InScope(async sp =>
    {
      var uow = sp.GetRequiredService<IUnitOfWork>();
      var penalty = sp.GetRequiredService<IPenaltyV1>();
      return await uow.Start(async () =>
      {
        var groups = await penalty.PayoutCandidates(1000);
        groups.Should().Contain(new PayoutGroup(charity, "EUR", 1100));
        return await penalty.ClaimForPayout(disbursement, charity, "EUR");
      });
    });
    stamped.Should().Be(1100);
    (await app.InScope(sp => sp.GetRequiredService<IPenaltyV1>().PayoutCandidates(1)))
      .Should().NotContain(g => g.CharityId == charity);

    await app.InScope(sp => sp.GetRequiredService<IPenaltyV1>().ReleasePayout(disbursement));
    (await app.InScope(sp => sp.GetRequiredService<IPenaltyV1>().PayoutCandidates(1)))
      .Should().Contain(new PayoutGroup(charity, "EUR", 1100));
  }
}

public sealed class PenaltyAccountTests(PenaltyApp app)
{
  [Fact]
  public async Task Delete_guard_blocks_with_409_while_owing()
  {
    var u = TestTokens.NewUser();
    app.Gateway.ByUser[u] = _ => new ChargeOutcome.NoConsent();
    await app.MissedPenalty(u, 1050);
    var act = () => app.InScope(async sp =>
    {
      foreach (var g in sp.GetServices<IDeleteGuard>()) await g.Check(u, Ct);
    });
    var ex = (await act.Should().ThrowAsync<AccountDeletionBlocked>()).Which;
    ex.Status.Should().Be(409);
    ex.Debt.TotalCents.Should().Be(1050);
  }

  [Fact]
  public async Task Delete_guard_passes_when_nothing_is_owed()
  {
    var u = TestTokens.NewUser();
    await app.MissedPenalty(u);
    await app.Drain();
    await app.InScope(async sp =>
    {
      foreach (var g in sp.GetServices<IDeleteGuard>().Where(g => g is NoDebtGuard)) await g.Check(u, Ct);
    });
  }

  [Fact]
  public async Task Purge_leaves_zero_rows_of_the_user_and_keeps_the_ledger()
  {
    var u = TestTokens.NewUser();
    var p = await app.MissedPenalty(u, 800);
    await app.Drain();
    await app.MissedPenalty(u, 100);

    await app.InScope(async sp =>
    {
      var uow = sp.GetRequiredService<IUnitOfWork>();
      await uow.Start(async () =>
      {
        foreach (var purge in sp.GetServices<IUserPurge>().Where(x => x is PenaltyPurge)) await purge.Purge(u, Ct);
      });
    });

    (await app.Count("SELECT count(*) FROM penalty.\"Penalties\" WHERE \"UserId\" = @u", ("u", u))).Should().Be(0);
    var kept = (await app.Read(p.Id))!;
    kept.UserId.Should().Be(Stake.DeletedUser);
    kept.AmountCents.Should().Be(800);
    kept.Status.Should().Be(PenaltyStatus.Charged);
  }
}
