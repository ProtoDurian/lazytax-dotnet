using System.Transactions;
using Ctx.Payment.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;
using Microsoft.Extensions.Logging;

namespace Ctx.Payment.Domain;

/// <summary>Customers, consents and interactive intents (LazyTax PaymentService minus the stored-consent charge).</summary>
public sealed class PaymentService(
  ICustomerRepository customers,
  IPaymentGateway gateway,
  IIdentityClaims claims,
  IConsentNotifier notifier,
  IUnitOfWork uow,
  TimeProvider clock,
  ILogger<PaymentService> logger)
{
  private static readonly ConsentPurpose[] AllPurposes = [ConsentPurpose.Penalty, ConsentPurpose.Subscription];

  /// <summary>LazyTax's 3-step create: DB, then Airwallex by merchant id, then a new Airwallex customer.</summary>
  public async Task<PaymentCustomer> CreateCustomer(string userId, CancellationToken ct)
  {
    NoAmbientTransaction();
    if (await customers.Get(Owner.User(userId), ct) is { } existing) return existing;
    var airwallexId = await Gateway(() => gateway.FindCustomer(userId, ct))
                      ?? await Gateway(() => gateway.CreateCustomer(userId, ct));
    return await customers.Create(userId, airwallexId, ct);
  }

  public async Task<(string ClientSecret, string CustomerId)> ClientSecret(Owner owner, CancellationToken ct)
  {
    var c = await this.Require(owner, ct);
    var secret = await Gateway(() => gateway.ClientSecret(c.AirwallexCustomerId, ct));
    return (secret, c.AirwallexCustomerId);
  }

  public async Task<ConsentState> GetConsent(Owner owner, ConsentPurpose purpose, CancellationToken ct) =>
    (await this.Require(owner, ct)).StateFor(purpose);

  public async Task<bool> HasConsent(string userId, ConsentPurpose purpose, CancellationToken ct) =>
    (await customers.Get(Owner.User(userId), ct))?.HasConsentFor(purpose) ?? false;

  /// <summary>User-initiated revoke: gateway first, then the local row (guarded on the revoked id), claim, email.</summary>
  public async Task DisableConsent(Owner owner, ConsentPurpose purpose, CancellationToken ct)
  {
    NoAmbientTransaction();
    var c = await this.Require(owner, ct);
    var consent = c.ConsentFor(purpose) ?? throw new NotFound("PaymentConsent", $"{c.UserId}:{purpose.Kind()}");
    await Gateway(() => gateway.DisableConsent(consent.ConsentId, ct));
    // The row and the email request commit together; the email leaves after commit (outbox).
    await uow.Start(async () =>
    {
      await customers.DeleteConsent(owner, purpose, consent.ConsentId, ct);
      await this.BestEffort(() => notifier.ConsentChanged(c.UserId, linked: false, purpose, clock.GetUtcNow().UtcDateTime, ct));
    }, ct);
    if (purpose == ConsentPurpose.Penalty) await this.BestEffort(() => claims.SetClaim(c.UserId, Claims.HasPaymentConsent, "false", ct));
  }

  /// <summary>
  /// Account deletion: revoke every consent, attempting each purpose even when one fails (a skipped mandate would
  /// stay live at Airwallex with its id purged). No email (LazyTax: deletion must not email).
  /// </summary>
  public async Task<bool> RevokeAll(string userId, CancellationToken ct)
  {
    NoAmbientTransaction();
    var c = await customers.Get(Owner.User(userId), ct);
    if (c is null) return true;
    var ok = true;
    foreach (var purpose in AllPurposes)
    {
      if (c.ConsentFor(purpose) is not { } consent) continue;
      try
      {
        await gateway.DisableConsent(consent.ConsentId, ct);
        await customers.DeleteConsent(Owner.User(userId), purpose, consent.ConsentId, ct);
      }
      catch (Exception e) when (e is not OperationCanceledException)
      {
        ok = false;
        logger.LogWarning(e, "RevokeAll: {Purpose} consent of {UserId} not revoked", purpose, userId);
      }
    }
    return ok;
  }

  public async Task<GatewayIntent> CreateIntent(Owner owner, decimal amount, string currency, string description, CancellationToken ct)
  {
    NoAmbientTransaction();
    var c = await this.Require(owner, ct);
    var key = Guid.NewGuid().ToString(); // interactive: fresh key, as LazyTax
    return await Gateway(() => gateway.CreateIntent(
      new IntentRequest(key, key, c.AirwallexCustomerId, amount, currency.ToUpperInvariant(), description), ct));
  }

  /// <summary>Confirms the owner's intent. Another customer's intent id is a 404 (layer 4 through the gateway).</summary>
  public async Task<GatewayIntent> ConfirmIntent(Owner owner, string intentId, string consentId, CancellationToken ct)
  {
    NoAmbientTransaction();
    var c = await this.Require(owner, ct);
    GatewayIntent intent;
    try
    {
      intent = await gateway.RetrieveIntent(intentId, ct);
    }
    catch (GatewayException e) when (e.NotFound)
    {
      throw new NotFound("PaymentIntent", intentId);
    }
    if (intent.CustomerId != c.AirwallexCustomerId) throw new NotFound("PaymentIntent", intentId);
    return await Gateway(() => gateway.ConfirmIntent(intent.Id, consentId, c.AirwallexCustomerId, ct));
  }

  /// <summary>Webhook payment_consent.*: store the consent, then (verified) email and set the Logto claim.</summary>
  public async Task<PaymentCustomer?> ApplyConsentEvent(
    string airwallexCustomerId, string? consentId, ConsentStatus? status, ConsentPurpose purpose, CancellationToken ct)
  {
    NoAmbientTransaction();
    var c = await uow.Start(async () =>
    {
      var updated = await customers.UpsertConsent(airwallexCustomerId, consentId, status, purpose, ct);
      // Webhook replays may repeat the email; acceptable for a security notice (as LazyTax).
      if (updated is not null && status == ConsentStatus.Verified)
        await this.BestEffort(() => notifier.ConsentChanged(updated.UserId, linked: true, purpose, clock.GetUtcNow().UtcDateTime, ct));
      return updated;
    }, ct);
    if (c is null)
    {
      logger.LogWarning("Consent event for unknown Airwallex customer {CustomerId}", airwallexCustomerId);
      return null;
    }
    // Logto is HTTP: after commit, best effort. The claim gates penalty flows only.
    if (status == ConsentStatus.Verified && purpose == ConsentPurpose.Penalty)
      await this.BestEffort(() => claims.SetClaim(c.UserId, Claims.HasPaymentConsent, "true", ct));
    return c;
  }

  private async Task<PaymentCustomer> Require(Owner owner, CancellationToken ct) =>
    await customers.Get(owner, ct) ?? throw new NotFound("PaymentCustomer", owner.ToString());

  private static async Task<T> Gateway<T>(Func<Task<T>> call)
  {
    try
    {
      return await call();
    }
    catch (GatewayException e)
    {
      throw new GatewayFailed(e.Message);
    }
  }

  private static Task Gateway(Func<Task> call) => Gateway(async () =>
  {
    await call();
    return true;
  });

  private async Task BestEffort(Func<Task> side)
  {
    try
    {
      await side();
    }
    catch (Exception e) when (e is not OperationCanceledException)
    {
      logger.LogWarning(e, "payment side effect failed (best effort)");
    }
  }

  private static void NoAmbientTransaction()
  {
    if (Transaction.Current is not null)
      throw new InvalidOperationException("this payment call reaches the gateway; call it outside any unit of work");
  }
}
