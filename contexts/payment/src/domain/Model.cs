using Ctx.Payment.Contract.V1;

namespace Ctx.Payment.Domain;

/// <summary>One stored, purpose-scoped consent (an Airwallex mandate) of a customer.</summary>
public sealed record StoredConsent(string ConsentId, ConsentStatus? Status)
{
  public bool Verified => this.Status == ConsentStatus.Verified;
}

/// <summary>The user's Airwallex customer and its consents (at most one per purpose).</summary>
public sealed record PaymentCustomer(
  Guid Id,
  string UserId,
  string AirwallexCustomerId,
  IReadOnlyDictionary<ConsentPurpose, StoredConsent> Consents,
  DateTime CreatedAt,
  DateTime UpdatedAt)
{
  public StoredConsent? ConsentFor(ConsentPurpose purpose) => this.Consents.GetValueOrDefault(purpose);

  public bool HasConsentFor(ConsentPurpose purpose) => this.ConsentFor(purpose)?.Verified == true;

  public ConsentState StateFor(ConsentPurpose purpose)
  {
    var c = this.ConsentFor(purpose);
    return new ConsentState(purpose, c?.ConsentId, c?.Status);
  }
}

/// <summary>Where a charge is in its three phases.</summary>
public enum ChargeState
{
  /// <summary>Row exists, no attempt claimed yet.</summary>
  New,

  /// <summary>An attempt is claimed; the gateway call is in flight or its outcome is ambiguous (re-drive the same attempt).</summary>
  Processing,

  /// <summary>The intent exists but is not settled.</summary>
  Pending,

  Succeeded,
  NoConsent,
  Failed,
}

/// <summary>One logical charge, keyed by the caller's request id (e.g. the penalty id).</summary>
public sealed class ChargeRecord
{
  public required Guid RequestId { get; init; }
  public required string UserId { get; init; }
  public required ConsentPurpose Purpose { get; init; }
  public required long AmountCents { get; init; }
  public required string Currency { get; init; }
  public string? Description { get; init; }
  public ChargeState State { get; set; }
  public int Attempt { get; set; }
  public string? IntentId { get; set; }
  public string? GatewayStatus { get; set; }
  public string? LastError { get; set; }

  /// <summary>While set and in the future, another caller holds the attempt; do not call the gateway.</summary>
  public DateTime? InFlightUntil { get; set; }

  public DateTime CreatedAt { get; init; }
  public DateTime UpdatedAt { get; set; }

  public ChargeResult ToResult() => new(this.RequestId, this.State switch
  {
    ChargeState.Succeeded => ChargeStatus.Succeeded,
    ChargeState.NoConsent => ChargeStatus.NoConsent,
    ChargeState.Failed => ChargeStatus.Failed,
    _ => ChargeStatus.Pending,
  }, this.IntentId, this.Attempt, this.GatewayStatus, this.LastError);

  /// <summary>The gateway idempotency key of the current attempt: <c>{requestId}-{attempt}</c> (nitroso's shape).</summary>
  public string IdempotencyKey => $"{this.RequestId}-{this.Attempt}";
}

public static class Purposes
{
  /// <summary>Lowercase purpose name used in events and query strings.</summary>
  public static string Kind(this ConsentPurpose p) => p == ConsentPurpose.Subscription ? "subscription" : "penalty";

  /// <summary>LazyTax's label in the consent-changed email.</summary>
  public static string Label(this ConsentPurpose p) => p == ConsentPurpose.Subscription ? "Subscription" : "Habit stakes";

  /// <summary>LazyTax's classification: merchant_trigger_reason "scheduled" = subscription, anything else = penalty.</summary>
  public static ConsentPurpose FromTriggerReason(string? reason) =>
    string.Equals(reason, "scheduled", StringComparison.OrdinalIgnoreCase) ? ConsentPurpose.Subscription : ConsentPurpose.Penalty;

  public static ConsentStatus? ParseStatus(string? s) => s switch
  {
    "VERIFIED" => ConsentStatus.Verified,
    "REQUIRES_PAYMENT_METHOD" => ConsentStatus.RequiresPaymentMethod,
    "REQUIRES_CUSTOMER_ACTION" => ConsentStatus.RequiresCustomerAction,
    _ => null,
  };
}

/// <summary>Minor units per currency (cents for most; none for JPY/KRW).</summary>
public static class Money
{
  private static readonly HashSet<string> ZeroDecimal = ["JPY", "KRW"];

  public static decimal ToMajor(long minor, string currency) =>
    ZeroDecimal.Contains(currency.ToUpperInvariant()) ? minor : minor / 100m;

  public static readonly IReadOnlySet<string> Supported = new HashSet<string>
  {
    "USD", "AUD", "EUR", "GBP", "JPY", "CAD", "CHF", "CNY", "SEK", "NZD",
    "MXN", "SGD", "HKD", "NOK", "ZAR", "TRY", "BRL", "INR", "KRW", "PLN",
  };
}
