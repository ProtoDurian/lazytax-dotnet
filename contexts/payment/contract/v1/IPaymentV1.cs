using LazyTax.Platform.Data;

namespace Ctx.Payment.Contract.V1;

/// <summary>
/// payment.v1: the user's Airwallex customer, their purpose-scoped consents (mandates) and merchant-initiated
/// charges against them (LazyTax <c>Domain/Payment</c>). The gateway is reached over HTTP, so
/// <see cref="Charge"/> and <see cref="RevokeAll"/> must be called OUTSIDE any unit of work; they open their
/// own short transactions around the gateway call.
/// </summary>
public interface IPaymentV1
{
  /// <summary>True when the user has a VERIFIED consent for <paramref name="purpose"/> (LazyTax HasPaymentConsentAsync).</summary>
  Task<bool> HasConsent(string userId, ConsentPurpose purpose, CancellationToken ct = default);

  /// <summary>The owner's consent for <paramref name="purpose"/>; 404 when the owner has no payment customer.</summary>
  Task<ConsentState> GetConsent(Owner owner, ConsentPurpose purpose, CancellationToken ct = default);

  /// <summary>
  /// Charge the user's stored consent, exactly once per <paramref name="requestId"/> (the caller's own id, e.g.
  /// the penalty id). Three phases, as nitroso's <c>Withdrawal.Approve</c>: (1) claim an attempt in a
  /// transaction, (2) call the gateway outside it with idempotency key <c>{requestId}-{attempt}</c>, (3) write the
  /// result back only if that attempt still holds the claim. Calling again with the same id reconciles the
  /// earlier intent instead of charging twice; once <see cref="ChargeStatus.Succeeded"/> it returns that result.
  /// Emits <see cref="PaymentEvents.Completed"/> on success and <see cref="PaymentEvents.Failed"/> on a
  /// definitive failure. Never throws for gateway or consent problems; read <see cref="ChargeResult.Status"/>.
  /// Throws <c>InvalidOperationException</c> when called inside a unit of work.
  /// </summary>
  Task<ChargeResult> Charge(
    Guid requestId,
    string userId,
    long amountCents,
    string currency,
    ConsentPurpose purpose,
    string? description = null,
    CancellationToken ct = default);

  /// <summary>The last known state of a charge, or null when <paramref name="requestId"/> was never charged.</summary>
  Task<ChargeResult?> GetCharge(Guid requestId, CancellationToken ct = default);

  /// <summary>
  /// Revoke every consent the user still has at the gateway and delete them locally (account deletion). Every
  /// purpose is attempted even when one fails; returns false when any revoke failed. Outside a unit of work.
  /// </summary>
  Task<bool> RevokeAll(string userId, CancellationToken ct = default);
}

/// <summary>Which charging relationship a consent authorizes (card networks' MIT classification).</summary>
public enum ConsentPurpose
{
  /// <summary>Unscheduled merchant-initiated: habit penalties (event driven, variable).</summary>
  Penalty,

  /// <summary>Recurring merchant-initiated: plan subscriptions (fixed cadence).</summary>
  Subscription,
}

public enum ConsentStatus
{
  Verified,
  RequiresPaymentMethod,
  RequiresCustomerAction,
}

/// <summary>A user's consent for one purpose. <see cref="ConsentId"/> null = none stored.</summary>
public sealed record ConsentState(ConsentPurpose Purpose, string? ConsentId, ConsentStatus? Status)
{
  public bool HasConsent => this.Status == ConsentStatus.Verified;
}

public enum ChargeStatus
{
  /// <summary>Money moved (gateway intent SUCCEEDED). Terminal.</summary>
  Succeeded,

  /// <summary>The intent exists but is not settled (REQUIRES_PAYMENT_METHOD / _CUSTOMER_ACTION / _CAPTURE); call again later.</summary>
  Pending,

  /// <summary>No customer or no verified consent for the purpose; nothing was sent to the gateway (LazyTax: penalty Skipped).</summary>
  NoConsent,

  /// <summary>The gateway call failed or was rejected; call again later with the same request id (the caller caps attempts).</summary>
  Failed,
}

/// <param name="RequestId">The caller's id for this logical charge.</param>
/// <param name="Status">Outcome of the latest attempt.</param>
/// <param name="IntentId">The gateway's payment intent id, once one was created.</param>
/// <param name="Attempt">How many attempts payment has made for this request id.</param>
/// <param name="GatewayStatus">The gateway's raw intent status (e.g. <c>SUCCEEDED</c>), when known.</param>
/// <param name="Error">Why the latest attempt did not succeed; never secrets.</param>
public sealed record ChargeResult(
  Guid RequestId,
  ChargeStatus Status,
  string? IntentId,
  int Attempt,
  string? GatewayStatus,
  string? Error);

/// <summary>Outbox events payment emits.</summary>
public static class PaymentEvents
{
  /// <summary>A charge succeeded (payload <see cref="PaymentSettled"/>). penalty and plan subscribe.</summary>
  public const string Completed = "payment.completed";

  /// <summary>A charge attempt failed definitively: no consent or a gateway failure (payload <see cref="PaymentSettled"/>).</summary>
  public const string Failed = "payment.failed";
}

/// <summary>
/// Payload of <see cref="PaymentEvents.Completed"/> and <see cref="PaymentEvents.Failed"/>.
/// <paramref name="Kind"/> is the purpose in lowercase: <c>penalty</c> or <c>subscription</c>.
/// </summary>
public sealed record PaymentSettled(
  Guid RequestId,
  string UserId,
  string Kind,
  string? IntentId,
  long AmountCents,
  string Currency,
  int Attempt,
  string? Error);
