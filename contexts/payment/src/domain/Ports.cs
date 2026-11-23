using Ctx.Payment.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;

namespace Ctx.Payment.Domain;

/// <summary>Airwallex (LazyTax IPaymentGateway). HTTP in production; a fake with no network here. Never call it inside a unit of work.</summary>
public interface IPaymentGateway
{
  Task<string?> FindCustomer(string merchantCustomerId, CancellationToken ct);

  Task<string> CreateCustomer(string merchantCustomerId, CancellationToken ct);

  Task<string> ClientSecret(string customerId, CancellationToken ct);

  Task DisableConsent(string consentId, CancellationToken ct);

  /// <summary>Same <see cref="IntentRequest.RequestId"/> twice returns the same intent (gateway idempotency).</summary>
  Task<GatewayIntent> CreateIntent(IntentRequest req, CancellationToken ct);

  /// <summary>Throws <see cref="GatewayException"/> with <c>NotFound</c> when the intent does not exist.</summary>
  Task<GatewayIntent> RetrieveIntent(string intentId, CancellationToken ct);

  Task<GatewayIntent> ConfirmIntent(string intentId, string consentId, string customerId, CancellationToken ct);
}

public sealed record IntentRequest(string RequestId, string MerchantOrderId, string CustomerId, decimal Amount, string Currency, string Description);

public sealed record GatewayIntent(string Id, string Status, decimal Amount, string Currency, string CustomerId, string MerchantOrderId)
{
  public bool Succeeded => this.Status == "SUCCEEDED";

  /// <summary>Terminal without money moving.</summary>
  public bool Dead => this.Status is "CANCELLED" or "FAILED";
}

/// <summary>
/// A gateway call failed. <see cref="Definitive"/> = the gateway rejected the request, so it proves nothing was
/// created (a 4xx on a first send). Anything else (timeout, 5xx) is ambiguous.
/// </summary>
public sealed class GatewayException(string message, bool definitive, bool notFound = false) : Exception(message)
{
  public bool Definitive { get; } = definitive;
  public bool NotFound { get; } = notFound;
}

/// <summary>502: the payment gateway failed (routes only; the contract never throws it).</summary>
public sealed class GatewayFailed(string detail)
  : Problem(502, "payment_gateway_error", "Payment Gateway Error", detail);

/// <summary>Logto custom claims (LazyTax IAuthManagement.SetClaim). HTTP in production; a fake here.</summary>
public interface IIdentityClaims
{
  Task SetClaim(string userId, string claim, string value, CancellationToken ct);
}

public static class Claims
{
  /// <summary>LazyTax LogtoClaims.HasPaymentConsent.</summary>
  public const string HasPaymentConsent = "has_payment_consent";
}

/// <summary>The consent-changed security email (LazyTax IEmailNotifier.NotifyConsentChanged). Best effort.</summary>
public interface IConsentNotifier
{
  Task ConsentChanged(string userId, bool linked, ConsentPurpose purpose, DateTime atUtc, CancellationToken ct);
}

public interface ICustomerRepository
{
  /// <summary>Owner-scoped. <see cref="Owner.Anyone"/> is refused.</summary>
  Task<PaymentCustomer?> Get(Owner owner, CancellationToken ct);

  Task<PaymentCustomer?> GetByAirwallexId(string airwallexCustomerId, CancellationToken ct);

  /// <summary>Creates the user's customer; returns the existing one when it already exists (unique user id).</summary>
  Task<PaymentCustomer> Create(string userId, string airwallexCustomerId, CancellationToken ct);

  /// <summary>Insert, replace or (null consent id) remove the purpose's consent. Null when no such customer.</summary>
  Task<PaymentCustomer?> UpsertConsent(string airwallexCustomerId, string? consentId, ConsentStatus? status, ConsentPurpose purpose, CancellationToken ct);

  /// <summary>Deletes the purpose's consent ONLY if it still holds <paramref name="expectedConsentId"/>.</summary>
  Task<bool> DeleteConsent(Owner owner, ConsentPurpose purpose, string expectedConsentId, CancellationToken ct);
}

public interface IChargeRepository
{
  /// <summary>Inserts the charge row when missing, then locks it (FOR UPDATE). Inside a unit of work.</summary>
  Task<ChargeRecord> EnsureLocked(ChargeRecord fresh, CancellationToken ct);

  /// <summary>Locks the row (FOR UPDATE), or null. Inside a unit of work.</summary>
  Task<ChargeRecord?> Lock(Guid requestId, CancellationToken ct);

  /// <summary>Locks the row holding <paramref name="intentId"/>, or null. Inside a unit of work.</summary>
  Task<ChargeRecord?> LockByIntent(string intentId, CancellationToken ct);

  Task<ChargeRecord?> Get(Guid requestId, CancellationToken ct);

  /// <summary>Writes a row returned by a Lock call.</summary>
  Task Save(ChargeRecord row, CancellationToken ct);
}
