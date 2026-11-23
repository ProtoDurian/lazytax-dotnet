using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ctx.Payment.Contract.V1;
using Ctx.Payment.Data;
using Ctx.Payment.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Lifecycle;
using LazyTax.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Payment.Tests.Integration;

public sealed class PaymentApiTests(KernelApp app)
{
  private const string Base = "/api/v1/Payment";

  private static CancellationToken Ct => TestContext.Current.CancellationToken;

  private FakeAirwallex Gateway => app.Services.GetRequiredService<FakeAirwallex>();

  private async Task<string> CreateCustomer(string user)
  {
    var res = await app.ClientAs(user).PutAsync($"{Base}/{user}/customers", null, Ct);
    res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync(Ct));
    var body = await res.Content.ReadFromJsonAsync<JsonElement>(Ct);
    body.GetProperty("clientSecret").GetString().Should().StartWith("cs_");
    return body.GetProperty("customerId").GetString()!;
  }

  private async Task<HttpResponseMessage> PostWebhook(string body, string? secret = null, long? atMs = null)
  {
    var ts = (atMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).ToString();
    var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/webhook")
    {
      Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };
    req.Headers.Add("x-timestamp", ts);
    req.Headers.Add("x-signature", WebhookService.Sign(secret ?? new PaymentSettings().WebhookSecret, ts, body));
    return await app.Anonymous().SendAsync(req, Ct);
  }

  private static string ConsentEvent(string customerId, string consentId, string status = "VERIFIED", string reason = "unscheduled") =>
    JsonSerializer.Serialize(new
    {
      id = "evt_" + Guid.NewGuid().ToString("N")[..8],
      name = status == "VERIFIED" ? "payment_consent.verified" : "payment_consent.verification_failed",
      data = new { @object = new { id = consentId, status, customer_id = customerId, merchant_trigger_reason = reason } },
    });

  private async Task<string> VerifiedUser(string reason = "unscheduled")
  {
    var user = TestTokens.NewUser();
    var cus = await CreateCustomer(user);
    var res = await PostWebhook(ConsentEvent(cus, "cst_" + user, reason: reason));
    res.StatusCode.Should().Be(HttpStatusCode.NoContent, await res.Content.ReadAsStringAsync(Ct));
    return user;
  }

  [Fact]
  public async Task Create_customer_is_idempotent_and_starts_without_consent()
  {
    var user = TestTokens.NewUser();
    var first = await CreateCustomer(user);
    var second = await CreateCustomer(user);
    second.Should().Be(first);

    var consent = await app.ClientAs(user).GetFromJsonAsync<JsonElement>($"{Base}/{user}/consent", Ct);
    consent.GetProperty("hasPaymentConsent").GetBoolean().Should().BeFalse();
    (await app.Count("""SELECT count(*) FROM payment."PaymentCustomers" WHERE "UserId" = @u""", ("u", user))).Should().Be(1);
  }

  [Fact]
  public async Task Signed_consent_webhook_stores_the_consent_and_sets_the_claim()
  {
    var user = await VerifiedUser();

    var consent = await app.ClientAs(user).GetFromJsonAsync<JsonElement>($"{Base}/{user}/consent?purpose=penalty", Ct);
    consent.GetProperty("hasPaymentConsent").GetBoolean().Should().BeTrue();
    consent.GetProperty("status").GetString().Should().Be("Verified");
    (await app.InScope(sp => sp.GetRequiredService<IPaymentV1>().HasConsent(user, ConsentPurpose.Penalty))).Should().BeTrue();
    (await app.InScope(sp => sp.GetRequiredService<IPaymentV1>().HasConsent(user, ConsentPurpose.Subscription))).Should().BeFalse();
    app.Services.GetRequiredService<FakeIdentityClaims>().Get(user, Claims.HasPaymentConsent).Should().Be("true");
  }

  [Fact]
  public async Task Scheduled_consent_is_the_subscription_consent()
  {
    var user = await VerifiedUser(reason: "scheduled");

    var sub = await app.ClientAs(user).GetFromJsonAsync<JsonElement>($"{Base}/{user}/consent?purpose=subscription", Ct);
    sub.GetProperty("hasPaymentConsent").GetBoolean().Should().BeTrue();
    var pen = await app.ClientAs(user).GetFromJsonAsync<JsonElement>($"{Base}/{user}/consent", Ct);
    pen.GetProperty("hasPaymentConsent").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task Webhook_with_a_bad_signature_is_401_and_changes_nothing()
  {
    var user = TestTokens.NewUser();
    var cus = await CreateCustomer(user);

    var res = await PostWebhook(ConsentEvent(cus, "cst_x"), secret: "not-the-secret");

    res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await app.Count("""SELECT count(*) FROM payment."PaymentConsents" WHERE "ConsentId" = 'cst_x'""")).Should().Be(0);
  }

  [Fact]
  public async Task Webhook_with_a_stale_timestamp_is_401()
  {
    var user = TestTokens.NewUser();
    var cus = await CreateCustomer(user);

    var res = await PostWebhook(ConsentEvent(cus, "cst_old"), atMs: DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds());

    res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task Invalid_purpose_is_400()
  {
    var user = TestTokens.NewUser();
    var res = await app.ClientAs(user).GetAsync($"{Base}/{user}/consent?purpose=bogus", Ct);
    res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task Disable_consent_revokes_at_the_gateway_and_locally()
  {
    var user = await VerifiedUser();

    var res = await app.ClientAs(user).DeleteAsync($"{Base}/{user}/consent?purpose=penalty", Ct);

    res.StatusCode.Should().Be(HttpStatusCode.NoContent, await res.Content.ReadAsStringAsync(Ct));
    this.Gateway.DisabledConsents.Should().Contain("cst_" + user);
    (await app.InScope(sp => sp.GetRequiredService<IPaymentV1>().HasConsent(user, ConsentPurpose.Penalty))).Should().BeFalse();
    app.Services.GetRequiredService<FakeIdentityClaims>().Get(user, Claims.HasPaymentConsent).Should().Be("false");
  }

  [Fact]
  public async Task Disable_without_a_consent_is_404()
  {
    var user = TestTokens.NewUser();
    await CreateCustomer(user);
    (await app.ClientAs(user).DeleteAsync($"{Base}/{user}/consent", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task Interactive_intent_create_then_confirm()
  {
    var user = await VerifiedUser();
    var client = app.ClientAs(user);

    var created = await client.PostAsJsonAsync($"{Base}/{user}/intent", new { amount = 12.5m, currency = "usd", description = "top up" }, Ct);
    created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync(Ct));
    var intentId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("paymentIntentId").GetString();

    var confirmed = await client.PostAsJsonAsync($"{Base}/{user}/intent/{intentId}", new { paymentConsentId = "cst_" + user }, Ct);
    confirmed.StatusCode.Should().Be(HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync(Ct));
    (await confirmed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString().Should().Be("SUCCEEDED");
  }

  [Fact]
  public async Task Intent_validation_is_400()
  {
    var user = TestTokens.NewUser();
    await CreateCustomer(user);
    var res = await app.ClientAs(user).PostAsJsonAsync($"{Base}/{user}/intent", new { amount = 0, currency = "XXX", description = "" }, Ct);
    res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  // ---- auth layers 1, 3, 4 ----

  [Fact]
  public async Task No_token_is_401()
  {
    var user = TestTokens.NewUser();
    (await app.Anonymous().GetAsync($"{Base}/{user}/consent", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await app.Anonymous().PutAsync($"{Base}/{user}/customers", null, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task Another_users_path_is_403()
  {
    var a = TestTokens.NewUser();
    var b = TestTokens.NewUser();
    await CreateCustomer(a);
    (await app.ClientAs(b).GetAsync($"{Base}/{a}/consent", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await app.ClientAs(b).DeleteAsync($"{Base}/{a}/consent", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await app.ClientAs(b).GetAsync($"{Base}/{a}/client-secret", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
  }

  [Fact]
  public Task Customer_of_another_user_is_404() => OwnerIsolation.AssertOtherUserGets404(app,
    createAsA: async (_, a) => await CreateCustomer(a),
    requestAsB: (b, _) => new HttpRequestMessage(HttpMethod.Get, $"{Base}/{b}/client-secret"));

  [Fact]
  public Task Consent_of_another_user_is_404() => OwnerIsolation.AssertOtherUserGets404(app,
    createAsA: async (_, a) =>
    {
      var cus = await CreateCustomer(a);
      await PostWebhook(ConsentEvent(cus, "cst_" + a));
      return "cst_" + a;
    },
    requestAsB: (b, _) => new HttpRequestMessage(HttpMethod.Delete, $"{Base}/{b}/consent"));

  [Fact]
  public async Task Intent_of_another_user_is_404_and_not_confirmed()
  {
    var a = await VerifiedUser();
    var created = await app.ClientAs(a).PostAsJsonAsync($"{Base}/{a}/intent", new { amount = 5m, currency = "USD", description = "a" }, Ct);
    var intentId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("paymentIntentId").GetString()!;
    var b = await VerifiedUser();

    var res = await app.ClientAs(b).PostAsJsonAsync($"{Base}/{b}/intent/{intentId}", new { paymentConsentId = "cst_" + b }, Ct);

    res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    this.Gateway.Calls.Should().NotContain($"confirm:{intentId}");
  }

  // ---- the contract ----

  [Fact]
  public async Task Charge_through_the_contract_moves_money_once_and_emits_completed()
  {
    var user = await VerifiedUser();
    var id = Guid.NewGuid();

    var first = await app.InScope(sp => sp.GetRequiredService<IPaymentV1>().Charge(id, user, 1500, "USD", ConsentPurpose.Penalty));
    var again = await app.InScope(sp => sp.GetRequiredService<IPaymentV1>().Charge(id, user, 1500, "USD", ConsentPurpose.Penalty));

    first.Status.Should().Be(ChargeStatus.Succeeded);
    again.IntentId.Should().Be(first.IntentId);
    this.Gateway.Calls.Count(c => c == $"create:{id}-1").Should().Be(1);
    (await app.Count("""SELECT count(*) FROM payment."Charges" WHERE "Id" = @id AND "State" = 'Succeeded'""", ("id", id))).Should().Be(1);
    (await app.Count("""SELECT count(*) FROM kernel.outbox_events WHERE name = 'payment.completed' AND payload->>'requestId' = @id""",
      ("id", id.ToString()))).Should().Be(1);
    var stored = await app.InScope(sp => sp.GetRequiredService<IPaymentV1>().GetCharge(id));
    stored!.Status.Should().Be(ChargeStatus.Succeeded);
  }

  [Fact]
  public async Task Concurrent_charges_of_one_request_move_money_once()
  {
    var user = await VerifiedUser();
    var id = Guid.NewGuid();

    var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
      app.InScope(sp => sp.GetRequiredService<IPaymentV1>().Charge(id, user, 700, "USD", ConsentPurpose.Penalty))));

    results.Select(r => r.IntentId).Where(i => i is not null).Distinct().Should().HaveCountLessThanOrEqualTo(1);
    this.Gateway.Calls.Count(c => c.StartsWith($"create:{id}-", StringComparison.Ordinal)).Should().Be(1);
    var final = await app.InScope(sp => sp.GetRequiredService<IPaymentV1>().GetCharge(id));
    final!.Status.Should().Be(ChargeStatus.Succeeded);
  }

  [Fact]
  public async Task Charge_without_consent_is_NoConsent_and_emits_failed()
  {
    var user = TestTokens.NewUser();
    var id = Guid.NewGuid();

    var res = await app.InScope(sp => sp.GetRequiredService<IPaymentV1>().Charge(id, user, 100, "USD", ConsentPurpose.Penalty));

    res.Status.Should().Be(ChargeStatus.NoConsent);
    (await app.Count("""SELECT count(*) FROM kernel.outbox_events WHERE name = 'payment.failed' AND payload->>'requestId' = @id""",
      ("id", id.ToString()))).Should().Be(1);
  }

  [Fact]
  public async Task Charge_inside_a_unit_of_work_is_refused()
  {
    var user = await VerifiedUser();
    var act = () => app.InScope(sp => sp.GetRequiredService<IUnitOfWork>().Start(() =>
      sp.GetRequiredService<IPaymentV1>().Charge(Guid.NewGuid(), user, 100, "USD", ConsentPurpose.Penalty)));
    await act.Should().ThrowAsync<InvalidOperationException>();
  }

  // ---- account deletion hooks ----

  [Fact]
  public async Task Purge_leaves_zero_payment_rows_of_the_user()
  {
    var user = await VerifiedUser();
    await app.InScope(sp => sp.GetRequiredService<IPaymentV1>().Charge(Guid.NewGuid(), user, 300, "USD", ConsentPurpose.Penalty));
    var other = await VerifiedUser();

    await app.InScope(sp => sp.GetRequiredService<IUnitOfWork>().Start(async () =>
    {
      foreach (var p in sp.GetServices<IUserPurge>().OfType<PaymentPurge>()) await p.Purge(user, CancellationToken.None);
    }));

    (await app.Count("""SELECT count(*) FROM payment."PaymentCustomers" WHERE "UserId" = @u""", ("u", user))).Should().Be(0);
    (await app.Count("""
      SELECT count(*) FROM payment."PaymentConsents" WHERE "ConsentId" = @c
      """, ("c", "cst_" + user))).Should().Be(0);
    (await app.Count("""SELECT count(*) FROM payment."Charges" WHERE "UserId" = @u""", ("u", user))).Should().Be(0);
    (await app.Count("""SELECT count(*) FROM payment."PaymentCustomers" WHERE "UserId" = @u""", ("u", other))).Should().Be(1);
  }

  [Fact]
  public async Task Before_purge_revokes_every_consent()
  {
    var user = TestTokens.NewUser();
    var cus = await CreateCustomer(user);
    await PostWebhook(ConsentEvent(cus, "cstp_" + user));
    await PostWebhook(ConsentEvent(cus, "csts_" + user, reason: "scheduled"));

    await app.InScope(async sp =>
    {
      foreach (var h in sp.GetServices<IBeforePurge>().OfType<RevokeConsentsBeforePurge>()) await h.Run(user, CancellationToken.None);
    });

    this.Gateway.DisabledConsents.Should().Contain(["cstp_" + user, "csts_" + user]);
    (await app.InScope(sp => sp.GetRequiredService<IPaymentV1>().HasConsent(user, ConsentPurpose.Subscription))).Should().BeFalse();
  }

  // ---- MCP ----

  [Fact]
  public async Task Mcp_consent_tool_answers_like_the_route()
  {
    var user = await VerifiedUser();
    var res = await app.ClientAs(user).PostAsJsonAsync("/mcp", new
    {
      jsonrpc = "2.0",
      id = 1,
      method = "tools/call",
      @params = new { name = "payment.consent", arguments = new { userId = user, purpose = "penalty" } },
    }, Ct);
    var body = await res.Content.ReadFromJsonAsync<JsonElement>(Ct);
    body.GetProperty("result").GetProperty("structuredContent").GetProperty("hasPaymentConsent").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task Mcp_consent_tool_refuses_another_user()
  {
    var a = await VerifiedUser();
    var b = TestTokens.NewUser();
    var res = await app.ClientAs(b).PostAsJsonAsync("/mcp", new
    {
      jsonrpc = "2.0",
      id = 1,
      method = "tools/call",
      @params = new { name = "payment.consent", arguments = new { userId = a } },
    }, Ct);
    var text = await res.Content.ReadAsStringAsync(Ct);
    text.Should().Contain("isError").And.NotContain("hasPaymentConsent\":true");
  }
}
