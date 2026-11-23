using System.Transactions;
using Ctx.Penalty.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using LazyTax.Platform.Problems;
using Microsoft.Extensions.Logging;

namespace Ctx.Penalty.Domain;

/// <summary>
/// LazyTax PenaltyService + PenaltyStakeBinding + UserService.GuardNoDebt. Writes run in units of work;
/// the charge itself (payment.v1, an HTTP call to the gateway) always runs outside one.
/// </summary>
public sealed class PenaltyService(
  IPenaltyStore store,
  IChargeGateway gateway,
  ICharityNames charities,
  IUnitOfWork uow,
  IOutbox outbox,
  PenaltyOptions options,
  TimeProvider clock,
  ILogger<PenaltyService> logger)
{
  private DateTime Now => clock.GetUtcNow().UtcDateTime;

  // ── check.settled (LazyTax PenaltyStakeBinding) ─────────────────────────────────────────────

  /// <summary>
  /// A missed check with a money stake and a charity enqueues one pending penalty, exactly once per
  /// execution. Returns true when a row was inserted.
  /// </summary>
  public Task<bool> OnSettled(SettledCheck e, CancellationToken ct)
  {
    if (!string.Equals(e.Status, "missed", StringComparison.OrdinalIgnoreCase) || e.ExecutionId is not { } executionId)
      return Task.FromResult(false);
    var amount = Stake.Amount(e.StakeCents, e.RatioBps);
    // No money stake, so nothing to charge (and no charity to give it to).
    if (amount <= 0 || e.CharityId is not { } charityId || string.IsNullOrWhiteSpace(e.Currency))
      return Task.FromResult(false);
    var p = new NewPenalty(executionId, e.HabitId, e.UserId, charityId, amount, e.Currency.ToUpperInvariant());
    return uow.Start(() => store.Enqueue(p, this.Now, ct), ct);
  }

  // ── The drain (LazyTax PenaltyService.ProcessPending) ───────────────────────────────────────

  /// <summary>
  /// Claims a batch in one unit of work, charges each OUTSIDE any unit, then settles each in its own unit.
  /// Returns how many penalties left pending this run (charged, failed or skipped).
  /// </summary>
  public async Task<int> ProcessPending(CancellationToken ct)
  {
    if (Transaction.Current is not null)
      throw new InvalidOperationException("ProcessPending must not run inside a unit of work (it charges over HTTP)");
    var now = this.Now;
    var claimed = await uow.Start(
      () => store.ClaimPending(options.BatchSize, now - TimeSpan.FromMinutes(options.ClaimLeaseMinutes), now, ct), ct);
    var done = 0;
    foreach (var p in claimed)
    {
      try
      {
        done += await this.ProcessOne(p, ct);
      }
      catch (OperationCanceledException)
      {
        throw; // a shutdown aborts the drain; the lease hands the rows back later
      }
      catch (Exception ex)
      {
        // Isolate per penalty: one bad row never strands the rest of the claimed batch.
        logger.LogError(ex, "Penalty {PenaltyId} threw while settling; isolating", p.Id);
        try
        {
          done += await this.Retry(p, $"unhandled: {ex.Message}", ct);
        }
        catch (Exception inner) when (inner is not OperationCanceledException)
        {
          logger.LogError(inner, "Could not record the failure of penalty {PenaltyId}", p.Id);
        }
      }
    }
    if (claimed.Count > 0 && logger.IsEnabled(LogLevel.Information))
      logger.LogInformation("Penalty drain claimed {Claimed}, settled {Done}", claimed.Count, done);
    return done;
  }

  private async Task<int> ProcessOne(PenaltyInfo p, CancellationToken ct)
  {
    ChargeOutcome outcome;
    try
    {
      outcome = await gateway.Charge(p, ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      outcome = new ChargeOutcome.Error(ex.Message);
    }

    switch (outcome)
    {
      case ChargeOutcome.Succeeded s:
        return await this.Charged(p.Id, s.PaymentIntentId, ct) ? 1 : 0;

      case ChargeOutcome.Pending pending:
        {
          // REQUIRES_PAYMENT_METHOD / _CUSTOMER_ACTION / _CAPTURE: not settled, retry; failed at the cap.
          var attempts = p.Attempts + 1;
          if (attempts >= options.MaxAttempts)
            return await this.Fail(p, $"Max attempts reached, last status {pending.Status}", ct);
          await uow.Start(() => store.MarkPending(p.Id, pending.PaymentIntentId, attempts, this.Now, ct), ct);
          return 0;
        }

      case ChargeOutcome.NoConsent:
        // No verified consent -> skipped (LazyTax: NotFoundException(PaymentCustomer)).
        return await uow.Start(() => store.MarkSkipped(p.Id, this.Now, ct), ct) ? 1 : 0;

      case ChargeOutcome.Error error:
        return await this.Retry(p, error.Message, ct);

      default:
        throw new InvalidOperationException($"unknown charge outcome {outcome}");
    }
  }

  /// <summary>A transient error: bump and keep pending, or failed at the attempt cap.</summary>
  private async Task<int> Retry(PenaltyInfo p, string error, CancellationToken ct)
  {
    if (p.Attempts + 1 >= options.MaxAttempts) return await this.Fail(p, error, ct);
    await uow.Start(() => store.Bump(p.Id, error, this.Now, ct), ct);
    return 0;
  }

  private async Task<int> Fail(PenaltyInfo p, string error, CancellationToken ct)
  {
    var name = await charities.NameOf(p.CharityId, ct);
    var failed = await uow.Start(async () =>
    {
      var now = this.Now;
      if (!await store.MarkFailed(p.Id, error, now, ct)) return false;
      await outbox.Add(PenaltyEvents.Failed, new PenaltyFailed(p.Id, p.UserId, p.HabitId, p.HabitExecutionId,
        p.CharityId, name, p.AmountCents, p.Currency, error, now), ct);
      return true;
    }, ct);
    return failed ? 1 : 0;
  }

  /// <summary>
  /// Charged + charity credit + <c>penalty.charged</c>, in one unit of work. Idempotent: a second call (the
  /// drain and the payment.completed event both seeing the same success) changes nothing.
  /// </summary>
  public async Task<bool> Charged(Guid penaltyId, string paymentIntentId, CancellationToken ct)
  {
    return await uow.Start(async () =>
    {
      var now = this.Now;
      var p = await store.MarkCharged(penaltyId, paymentIntentId, now, ct);
      if (p is null) return false;
      var name = await charities.NameOf(p.CharityId, ct);
      await outbox.Add(PenaltyEvents.Charged, new PenaltyCharged(p.Id, p.UserId, p.HabitId, p.HabitExecutionId,
        p.CharityId, name, p.AmountCents, p.Currency, paymentIntentId, now), ct);
      return true;
    }, ct);
  }

  // ── Reads ───────────────────────────────────────────────────────────────────────────────────

  public async Task<OutstandingDebt> Outstanding(string userId, CancellationToken ct)
  {
    var rows = await store.Unpaid(userId, ct);
    var by = rows.Where(r => r.TotalCents > 0).Select(r => new CurrencyAmount(r.Currency, r.TotalCents)).ToList();
    var first = by.FirstOrDefault();
    return new OutstandingDebt(first?.TotalCents ?? 0, first?.Currency ?? string.Empty, by, rows.Sum(r => r.Count));
  }

  /// <summary>LazyTax UserService.GuardNoDebt: 409 while anything is owed (when the flag is on).</summary>
  public async Task GuardNoDebt(string userId, CancellationToken ct)
  {
    if (!options.BlockDeletionOnDebt) return;
    var debt = await this.Outstanding(userId, ct);
    if (debt.TotalCents > 0) throw new AccountDeletionBlocked(debt);
  }

  public Task<IReadOnlyList<PenaltyInfo>> Search(Owner owner, PenaltySearch search, CancellationToken ct)
  {
    if (search.Status is { } s && !PenaltyStatus.All.Contains(s))
      throw new ValidationFailed("Invalid penalty search", "status", $"must be one of {string.Join(", ", PenaltyStatus.All)}");
    if (search.Limit is < 1 or > 100)
      throw new ValidationFailed("Invalid penalty search", "limit", "must be between 1 and 100");
    if (search.Skip < 0)
      throw new ValidationFailed("Invalid penalty search", "skip", "must be 0 or more");
    return store.Search(owner, search, ct);
  }

  public Task<PenaltyInfo?> Get(Owner owner, Guid id, CancellationToken ct) => store.Get(owner, id, ct);

  /// <summary>404 when the penalty is not the owner's.</summary>
  public async Task<PenaltyInfo> GetOwned(Owner owner, Guid id, CancellationToken ct) =>
    await store.Get(owner, id, ct) ?? throw new NotFound("Penalty", id.ToString());

  public Task<IReadOnlyList<CharityBalance>> CharityBalances(CancellationToken ct) => store.CharityBalances(ct);

  // ── Payout (disbursement) ───────────────────────────────────────────────────────────────────

  public Task<IReadOnlyList<PayoutGroup>> PayoutCandidates(long minPayoutCents, CancellationToken ct) =>
    uow.Start(() => store.PayoutCandidates(minPayoutCents, ct), ct);

  public Task<long> ClaimForPayout(Guid disbursementId, Guid charityId, string currency, CancellationToken ct) =>
    uow.Start(() => store.ClaimForPayout(disbursementId, charityId, currency, this.Now, ct), ct);

  public Task ReleasePayout(Guid disbursementId, CancellationToken ct) =>
    uow.Start(() => store.ReleasePayout(disbursementId, this.Now, ct), ct);
}

/// <summary>The fields of <c>check.settled</c> penalty reads (its own record; no reference to check's contract).</summary>
public sealed record SettledCheck(
  string UserId,
  Guid HabitId,
  Guid? ExecutionId,
  string Status,
  long StakeCents,
  long RatioBps,
  string? Currency,
  Guid? CharityId);
