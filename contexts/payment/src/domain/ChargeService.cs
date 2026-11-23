using System.Transactions;
using Ctx.Payment.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using Microsoft.Extensions.Logging;

namespace Ctx.Payment.Domain;

/// <summary>
/// Merchant-initiated charges against a stored consent, exactly once per request id, in nitroso's three phases
/// (<c>zinc/Domain/Withdrawal/Service.cs</c> Approve):
/// 1. claim an attempt in ONE short transaction (row lock on the charge);
/// 2. call the gateway OUTSIDE any transaction, idempotency key <c>{requestId}-{attempt}</c>;
/// 3. write the outcome back in a second transaction, only if this attempt still holds the claim.
/// A re-call after an intent exists reconciles that intent (retrieve; confirm only if not settled), so money
/// never moves twice (LazyTax ChargeStoredConsentAsync's existingIntentId path).
/// </summary>
public sealed class ChargeService(
  IChargeRepository charges,
  ICustomerRepository customers,
  IPaymentGateway gateway,
  IUnitOfWork uow,
  IOutbox outbox,
  TimeProvider clock,
  ILogger<ChargeService> logger)
{
  /// <summary>How long a claimed attempt blocks a concurrent caller from calling the gateway too.</summary>
  public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

  private sealed record Claim(ChargeRecord Row, PaymentCustomer? Customer, bool CallGateway);

  public async Task<ChargeResult> Charge(
    Guid requestId, string userId, long amountCents, string currency, ConsentPurpose purpose, string? description,
    CancellationToken ct)
  {
    if (Transaction.Current is not null)
      throw new InvalidOperationException("payment.Charge calls the gateway; call it outside any unit of work");
    ArgumentException.ThrowIfNullOrWhiteSpace(userId);
    if (amountCents <= 0) throw new ArgumentOutOfRangeException(nameof(amountCents), "must be > 0");
    currency = currency.ToUpperInvariant();

    // Phase 1: claim.
    var claim = await uow.Start(async () =>
    {
      var now = clock.GetUtcNow().UtcDateTime;
      var row = await charges.EnsureLocked(new ChargeRecord
      {
        RequestId = requestId,
        UserId = userId,
        Purpose = purpose,
        AmountCents = amountCents,
        Currency = currency,
        Description = description,
        State = ChargeState.New,
        CreatedAt = now,
        UpdatedAt = now,
      }, ct);

      if (row.UserId != userId || row.AmountCents != amountCents || row.Currency != currency || row.Purpose != purpose)
        throw new InvalidOperationException($"charge {requestId} was first requested with other terms");
      if (row.State == ChargeState.Succeeded) return new Claim(row, null, false);
      if (row.InFlightUntil is { } until && until > now) return new Claim(row, null, false); // another caller is on it

      var customer = await customers.Get(Owner.User(userId), ct);
      if (customer is null || !customer.HasConsentFor(purpose))
      {
        row.Attempt++;
        row.State = ChargeState.NoConsent;
        row.LastError = customer is null ? "no payment customer" : $"no verified {purpose.Kind()} consent";
        row.UpdatedAt = now;
        await charges.Save(row, ct);
        await this.Emit(PaymentEvents.Failed, row, ct);
        return new Claim(row, customer, false);
      }

      // A Processing row without an intent had an ambiguous first send: re-drive the SAME attempt (same key),
      // so the gateway collapses it into the original. Otherwise mint the next attempt.
      if (!(row.State == ChargeState.Processing && row.IntentId is null)) row.Attempt++;
      row.State = ChargeState.Processing;
      row.InFlightUntil = now + Lease;
      row.LastError = null;
      row.UpdatedAt = now;
      await charges.Save(row, ct);
      return new Claim(row, customer, true);
    }, ct);

    if (!claim.CallGateway) return claim.Row.ToResult();

    // Phase 2: the gateway, outside any transaction.
    var row = claim.Row;
    var customer = claim.Customer!;
    var consentId = customer.ConsentFor(purpose)!.ConsentId;
    GatewayIntent? intent = null;
    string? error = null;
    var definitive = false;
    try
    {
      if (row.IntentId is { } existing)
      {
        intent = await gateway.RetrieveIntent(existing, ct);
        if (!intent.Succeeded && !intent.Dead)
          intent = await gateway.ConfirmIntent(intent.Id, consentId, customer.AirwallexCustomerId, ct);
      }
      else
      {
        intent = await gateway.CreateIntent(new IntentRequest(
          row.IdempotencyKey, row.IdempotencyKey, customer.AirwallexCustomerId,
          Money.ToMajor(row.AmountCents, row.Currency), row.Currency,
          row.Description ?? $"LazyTax {purpose.Kind()} {row.RequestId}"), ct);
        if (!intent.Succeeded && !intent.Dead)
        {
          // Persist the intent id BEFORE confirming, so a confirm failure or crash is reconciled next time.
          await this.RecordIntent(row.RequestId, row.Attempt, intent.Id, ct);
          intent = await gateway.ConfirmIntent(intent.Id, consentId, customer.AirwallexCustomerId, ct);
        }
      }
    }
    catch (GatewayException e)
    {
      error = e.Message;
      definitive = e.Definitive;
      logger.LogWarning("Charge {RequestId} attempt {Attempt}: gateway failed ({Definitive}): {Error}",
        row.RequestId, row.Attempt, e.Definitive ? "rejected" : "ambiguous", e.Message);
    }

    // Phase 3: write back, only if this attempt still holds the claim.
    return await uow.Start(async () =>
    {
      var now = clock.GetUtcNow().UtcDateTime;
      var current = await charges.Lock(row.RequestId, ct) ?? throw new InvalidOperationException($"charge {row.RequestId} vanished");
      if (current.Attempt != row.Attempt || current.State != ChargeState.Processing)
        return current.ToResult(); // superseded (e.g. a webhook settled it, or a newer attempt): never overwrite

      current.InFlightUntil = null;
      current.UpdatedAt = now;
      if (error is null && intent is not null)
      {
        current.IntentId = intent.Id;
        current.GatewayStatus = intent.Status;
        current.State = intent.Succeeded ? ChargeState.Succeeded : intent.Dead ? ChargeState.Failed : ChargeState.Pending;
        current.LastError = intent.Succeeded ? null : $"intent {intent.Status}";
      }
      else if ((current.IntentId ??= intent?.Id) is null && !definitive)
      {
        // Ambiguous first send: the intent may exist under this attempt's key. Stay Processing so the next
        // call re-drives the same key instead of minting a second intent.
        current.LastError = error;
      }
      else
      {
        current.State = ChargeState.Failed;
        current.LastError = error;
      }

      await charges.Save(current, ct);
      if (current.State == ChargeState.Succeeded) await this.Emit(PaymentEvents.Completed, current, ct);
      else if (current.State is ChargeState.Failed or ChargeState.Processing) await this.Emit(PaymentEvents.Failed, current, ct);
      return current.ToResult();
    }, ct);
  }

  /// <summary>The webhook says an intent succeeded (Airwallex payment_intent.succeeded). Idempotent.</summary>
  public Task<bool> SettleFromGateway(string intentId, CancellationToken ct) =>
    uow.Start(async () =>
    {
      var row = await charges.LockByIntent(intentId, ct);
      if (row is null || row.State == ChargeState.Succeeded) return false;
      row.State = ChargeState.Succeeded;
      row.GatewayStatus = "SUCCEEDED";
      row.LastError = null;
      row.InFlightUntil = null;
      row.UpdatedAt = clock.GetUtcNow().UtcDateTime;
      await charges.Save(row, ct);
      await this.Emit(PaymentEvents.Completed, row, ct);
      return true;
    }, ct);

  public async Task<ChargeResult?> Get(Guid requestId, CancellationToken ct) => (await charges.Get(requestId, ct))?.ToResult();

  private Task RecordIntent(Guid requestId, int attempt, string intentId, CancellationToken ct) =>
    uow.Start(async () =>
    {
      var current = await charges.Lock(requestId, ct);
      if (current is null || current.Attempt != attempt || current.State != ChargeState.Processing) return;
      current.IntentId = intentId;
      current.UpdatedAt = clock.GetUtcNow().UtcDateTime;
      await charges.Save(current, ct);
    }, ct);

  private Task Emit(string name, ChargeRecord row, CancellationToken ct) =>
    outbox.Add(name, new PaymentSettled(row.RequestId, row.UserId, row.Purpose.Kind(), row.IntentId,
      row.AmountCents, row.Currency, row.Attempt, row.LastError), ct);
}
