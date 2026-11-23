using System.Security.Cryptography;
using System.Text;
using Ctx.Payment.Contract.V1;
using LazyTax.Platform.Problems;
using Microsoft.Extensions.Logging;

namespace Ctx.Payment.Domain;

/// <summary>Settings under <c>Contexts:payment</c>; defaults in code (no shared config edit needed).</summary>
public sealed class PaymentSettings
{
  /// <summary>Airwallex webhook secret (LazyTax Payment:Airwallex:Webhook). The dev default is not a secret.</summary>
  public string WebhookSecret { get; set; } = "whsec_lazytax_dev";

  /// <summary>How far x-timestamp may be from now (both ways).</summary>
  public int WebhookToleranceSeconds { get; set; } = 300;
}

/// <summary>The parts of an Airwallex event payment reads.</summary>
public sealed record WebhookEvent(
  string Name,
  string ObjectId,
  string? Status,
  string? CustomerId,
  string? MerchantTriggerReason);

public sealed class WebhookService(
  PaymentSettings settings,
  PaymentService payments,
  ChargeService charges,
  TimeProvider clock,
  ILogger<WebhookService> logger)
{
  /// <summary>HMAC-SHA256(secret, timestamp + raw body), lowercase hex (Airwallex's scheme, LazyTax AirwallexHmacCalculator).</summary>
  public static string Sign(string secret, string timestamp, string body)
  {
    var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(timestamp + body));
    return Convert.ToHexStringLower(hash);
  }

  /// <summary>401 unless the signature matches (constant-time) and the timestamp (epoch ms) is fresh.</summary>
  public void Verify(string? timestamp, string? signature, string body)
  {
    if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signature) || !long.TryParse(timestamp, out var ms))
      throw new Unauthenticated("Missing or malformed webhook signature");
    var skew = Math.Abs(clock.GetUtcNow().ToUnixTimeMilliseconds() - ms);
    if (skew > settings.WebhookToleranceSeconds * 1000L)
      throw new Unauthenticated("Stale webhook timestamp");
    var expected = Encoding.ASCII.GetBytes(Sign(settings.WebhookSecret, timestamp, body));
    var given = Encoding.ASCII.GetBytes(signature.ToLowerInvariant());
    if (!CryptographicOperations.FixedTimeEquals(expected, given))
      throw new Unauthenticated("Incorrect Signature");
  }

  public async Task Handle(WebhookEvent evt, CancellationToken ct)
  {
    switch (evt.Name)
    {
      case "payment_consent.verified" or "payment_consent.verification_failed":
        if (string.IsNullOrEmpty(evt.MerchantTriggerReason))
          logger.LogWarning("Consent {ConsentId} carries no merchant_trigger_reason; classified as penalty", evt.ObjectId);
        await payments.ApplyConsentEvent(evt.CustomerId ?? string.Empty, evt.ObjectId,
          Purposes.ParseStatus(evt.Status), Purposes.FromTriggerReason(evt.MerchantTriggerReason), ct);
        break;
      case "payment_intent.succeeded":
        await charges.SettleFromGateway(evt.ObjectId, ct);
        break;
      default:
        logger.LogWarning("Unknown webhook event type: {EventName}", evt.Name);
        break;
    }
  }
}

/// <summary>The contract, delegating to the services.</summary>
public sealed class PaymentV1(PaymentService payments, ChargeService charges) : IPaymentV1
{
  public Task<bool> HasConsent(string userId, ConsentPurpose purpose, CancellationToken ct = default) =>
    payments.HasConsent(userId, purpose, ct);

  public Task<ConsentState> GetConsent(LazyTax.Platform.Data.Owner owner, ConsentPurpose purpose, CancellationToken ct = default) =>
    payments.GetConsent(owner, purpose, ct);

  public Task<ChargeResult> Charge(Guid requestId, string userId, long amountCents, string currency, ConsentPurpose purpose,
    string? description = null, CancellationToken ct = default) =>
    charges.Charge(requestId, userId, amountCents, currency, purpose, description, ct);

  public Task<ChargeResult?> GetCharge(Guid requestId, CancellationToken ct = default) => charges.Get(requestId, ct);

  public Task<bool> RevokeAll(string userId, CancellationToken ct = default) => payments.RevokeAll(userId, ct);
}
