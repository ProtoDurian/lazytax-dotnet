using System.Text.Json;
using System.Text.Json.Serialization;
using Ctx.Payment.Contract.V1;
using Ctx.Payment.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using LazyTax.Platform.Problems;
using Microsoft.AspNetCore.Mvc;

namespace Ctx.Payment.Api.V1;

/// <summary>LazyTax's PaymentController, same paths under /api/v1/Payment.</summary>
[Route("")]
public sealed class PaymentController(PaymentService payments, WebhookService webhooks) : ContextController
{
  [Policy(Policies.Registered), Subject("userId")]
  [HttpPut("{userId}/customers")]
  public async Task<CreateCustomerRes> CreateCustomer(string userId, CancellationToken ct)
  {
    var customer = await payments.CreateCustomer(userId, ct);
    var (secret, _) = await payments.ClientSecret(Owner.User(userId), ct);
    return new CreateCustomerRes(customer.AirwallexCustomerId, secret);
  }

  [Policy(Policies.Registered), Subject("userId")]
  [HttpGet("{userId}/client-secret")]
  public async Task<ClientSecretRes> GetClientSecret(string userId, CancellationToken ct)
  {
    var (secret, customerId) = await payments.ClientSecret(Owner.User(userId), ct);
    return new ClientSecretRes(secret, customerId);
  }

  [Policy(Policies.Registered), Subject("userId")]
  [HttpGet("{userId}/consent")]
  public async Task<PaymentConsentRes> GetPaymentConsent(string userId, [FromQuery] string? purpose, CancellationToken ct) =>
    PaymentConsentRes.From(await payments.GetConsent(Owner.User(userId), PurposeParam.Parse(purpose), ct));

  [Policy(Policies.Registered), Subject("userId")]
  [HttpDelete("{userId}/consent")]
  public async Task<IActionResult> DisablePaymentConsent(string userId, [FromQuery] string? purpose, CancellationToken ct)
  {
    await payments.DisableConsent(Owner.User(userId), PurposeParam.Parse(purpose), ct);
    return this.NoContent();
  }

  [Policy(Policies.Registered), Subject("userId")]
  [HttpPost("{userId}/intent")]
  public async Task<PaymentIntentRes> CreatePaymentIntent(string userId, [FromBody] CreatePaymentIntentReq req, CancellationToken ct)
  {
    req.Validate();
    return PaymentIntentRes.From(await payments.CreateIntent(Owner.User(userId), req.Amount, req.Currency, req.Description, ct));
  }

  [Policy(Policies.Registered), Subject("userId")]
  [HttpPost("{userId}/intent/{intentId}")]
  public async Task<PaymentIntentRes> ConfirmPaymentIntent(string userId, string intentId, [FromBody] ConfirmPaymentIntentReq req,
    CancellationToken ct)
  {
    req.Validate();
    return PaymentIntentRes.From(await payments.ConfirmIntent(Owner.User(userId), intentId, req.PaymentConsentId, ct));
  }

  /// <summary>Airwallex webhook: public, authenticated by HMAC over the RAW body plus a fresh timestamp.</summary>
  [Policy(Policies.Public)]
  [HttpPost("webhook")]
  public async Task<IActionResult> Webhook(CancellationToken ct)
  {
    using var reader = new StreamReader(this.Request.Body);
    var body = await reader.ReadToEndAsync(ct);
    webhooks.Verify(this.Request.Headers["x-timestamp"].ToString(), this.Request.Headers["x-signature"].ToString(), body);
    AirwallexEvent? evt;
    try
    {
      evt = JsonSerializer.Deserialize<AirwallexEvent>(body);
    }
    catch (JsonException)
    {
      throw new ValidationFailed("Invalid webhook body", "body", "not JSON");
    }
    if (evt is null || string.IsNullOrEmpty(evt.Name)) throw new ValidationFailed("Invalid webhook body", "name", "required");
    var o = evt.Data?.Object;
    await webhooks.Handle(new WebhookEvent(evt.Name, o?.Id ?? string.Empty, o?.Status, o?.CustomerId, o?.MerchantTriggerReason), ct);
    return this.NoContent();
  }
}

public static class PurposeParam
{
  public static ConsentPurpose Parse(string? purpose) => purpose?.ToLowerInvariant() switch
  {
    null or "" or "penalty" => ConsentPurpose.Penalty,
    "subscription" => ConsentPurpose.Subscription,
    _ => throw new ValidationFailed("Invalid purpose", "purpose", "must be 'penalty' or 'subscription'"),
  };
}

public sealed record CreatePaymentIntentReq(decimal Amount, string Currency, string Description)
{
  public void Validate()
  {
    var errors = new Dictionary<string, string[]>();
    if (this.Amount <= 0) errors["amount"] = ["Amount must be greater than 0"];
    else if (this.Amount > 10000) errors["amount"] = ["Amount must not exceed 10,000"];
    if (string.IsNullOrWhiteSpace(this.Currency)) errors["currency"] = ["Currency is required"];
    else if (!Money.Supported.Contains(this.Currency.ToUpperInvariant()))
      errors["currency"] = ["Currency must be a valid ISO currency code (e.g., USD, AUD, EUR)"];
    if (string.IsNullOrWhiteSpace(this.Description)) errors["description"] = ["Description is required"];
    else if (this.Description.Length > 500) errors["description"] = ["Description must not exceed 500 characters"];
    if (errors.Count > 0) throw new ValidationFailed("Invalid CreatePaymentIntentReq", errors);
  }
}

public sealed record ConfirmPaymentIntentReq(string PaymentConsentId)
{
  public void Validate()
  {
    if (string.IsNullOrWhiteSpace(this.PaymentConsentId))
      throw new ValidationFailed("Invalid ConfirmPaymentIntentReq", "paymentConsentId", "PaymentConsentId is required");
    if (this.PaymentConsentId.Length > 100)
      throw new ValidationFailed("Invalid ConfirmPaymentIntentReq", "paymentConsentId", "PaymentConsentId must not exceed 100 characters");
  }
}

public sealed record CreateCustomerRes(string CustomerId, string ClientSecret);

public sealed record ClientSecretRes(string ClientSecret, string CustomerId);

public sealed record PaymentConsentRes(bool HasPaymentConsent, string? ConsentId, string? Status)
{
  public static PaymentConsentRes From(ConsentState s) => new(s.HasConsent, s.ConsentId, s.Status?.ToString());
}

/// <summary>LazyTax's CreatePaymentIntentRes and ConfirmPaymentIntentRes (same fields).</summary>
public sealed record PaymentIntentRes(string PaymentIntentId, string Status, decimal Amount, string Currency)
{
  public static PaymentIntentRes From(GatewayIntent i) => new(i.Id, i.Status, i.Amount, i.Currency);
}

public sealed record AirwallexEvent
{
  [JsonPropertyName("id")]
  public string? Id { get; init; }

  [JsonPropertyName("name")]
  public string Name { get; init; } = string.Empty;

  [JsonPropertyName("data")]
  public AirwallexEventData? Data { get; init; }
}

public sealed record AirwallexEventData
{
  [JsonPropertyName("object")]
  public AirwallexEventObject? Object { get; init; }
}

public sealed record AirwallexEventObject
{
  [JsonPropertyName("id")]
  public string? Id { get; init; }

  [JsonPropertyName("status")]
  public string? Status { get; init; }

  [JsonPropertyName("customer_id")]
  public string? CustomerId { get; init; }

  [JsonPropertyName("merchant_trigger_reason")]
  public string? MerchantTriggerReason { get; init; }
}
