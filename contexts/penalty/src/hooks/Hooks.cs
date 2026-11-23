using Ctx.Penalty.Domain;
using LazyTax.Platform.Lifecycle;
using LazyTax.Platform.Outbox;
using LazyTax.Platform.Workers;

namespace Ctx.Penalty.Hooks;

/// <summary>LazyTax UserService.GuardNoDebt: account deletion is blocked (409) while the user owes money.</summary>
internal sealed class NoDebtGuard(PenaltyService service) : IDeleteGuard
{
  public Task Check(string userId, CancellationToken ct) => service.GuardNoDebt(userId, ct);
}

/// <summary>
/// Anonymise-retain (LazyTax's AnonymizePenaltyLedger seam): the money ledger is an accounting record, so the
/// rows stay with amount, charity and intent id, and lose the user id. CharityBalances hold no user data.
/// </summary>
internal sealed class PenaltyPurge(IPenaltyStore store) : IUserPurge
{
  public Task Purge(string userId, CancellationToken ct) => store.Anonymise(userId, ct);
}

/// <summary>LazyTax PenaltyStakeBinding: a missed check enqueues one pending penalty.</summary>
internal sealed class CheckSettledHandler(PenaltyService service) : IOutboxSubscriber<SettledCheck>
{
  public Task Handle(OutboxEvent meta, SettledCheck e, CancellationToken ct) => service.OnSettled(e, ct);
}

/// <summary>The fields of payment.completed penalty reads.</summary>
public sealed record PaymentCompleted(Guid RequestId, string Kind, string? IntentId);

/// <summary>A penalty charge that settled after the drain's call returned (the gateway webhook): mark it charged.</summary>
internal sealed class PaymentCompletedHandler(PenaltyService service) : IOutboxSubscriber<PaymentCompleted>
{
  public async Task Handle(OutboxEvent meta, PaymentCompleted e, CancellationToken ct)
  {
    if (!string.Equals(e.Kind, "penalty", StringComparison.OrdinalIgnoreCase)) return;
    await service.Charged(e.RequestId, e.IntentId ?? e.RequestId.ToString(), ct);
  }
}

/// <summary>LazyTax PenaltyProcessorHostedService: drain pending penalties every 15 minutes.</summary>
internal sealed class PenaltyProcessorWorker(PenaltyService service, PenaltyOptions options) : IContextWorker
{
  public TimeSpan Interval => TimeSpan.FromMinutes(options.IntervalMinutes);

  public Task Tick(CancellationToken ct) => service.ProcessPending(ct);
}
