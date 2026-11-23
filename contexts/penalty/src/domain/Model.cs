using Ctx.Penalty.Contract.V1;
using LazyTax.Platform.Data;

namespace Ctx.Penalty.Domain;

/// <summary>Settings under <c>Contexts:penalty</c>. Defaults are LazyTax's.</summary>
public sealed class PenaltyOptions
{
  /// <summary>Rows claimed per drain pass (LazyTax ProcessPending batchSize).</summary>
  public int BatchSize { get; set; } = 100;

  /// <summary>Attempts before a penalty is marked failed (LazyTax maxAttempts).</summary>
  public int MaxAttempts { get; set; } = 5;

  /// <summary>A processing row older than this is re-claimed (LazyTax ClaimLease, 10 min).</summary>
  public int ClaimLeaseMinutes { get; set; } = 10;

  /// <summary>Drain interval (LazyTax PenaltyProcessorHostedService, 15 min).</summary>
  public int IntervalMinutes { get; set; } = 15;

  /// <summary>LazyTax BlockAccountDeletionOnDebt.</summary>
  public bool BlockDeletionOnDebt { get; set; } = true;
}

/// <summary>A penalty to enqueue for one missed execution.</summary>
public sealed record NewPenalty(Guid HabitExecutionId, Guid HabitId, string UserId, Guid CharityId, long AmountCents, string Currency);

/// <summary>What one charge call left behind (from payment.v1).</summary>
public abstract record ChargeOutcome
{
  /// <summary>Money moved.</summary>
  public sealed record Succeeded(string PaymentIntentId) : ChargeOutcome;

  /// <summary>Not settled yet (requires action, or a call that may still have moved money): retry with the same id.</summary>
  public sealed record Pending(string? PaymentIntentId, string Status) : ChargeOutcome;

  /// <summary>No verified consent: LazyTax marks the penalty skipped.</summary>
  public sealed record NoConsent : ChargeOutcome;

  /// <summary>Declined or a transient error: retry, failed at the attempt cap.</summary>
  public sealed record Error(string Message) : ChargeOutcome;
}

/// <summary>
/// Port to payment.v1. payment runs the 3 phases (claim, gateway call outside any unit of work with key
/// <c>{requestId}-{attempt}</c>, write-back); penalty passes its own id as the requestId, so a retry
/// reconciles the same gateway intent and never charges twice. Called OUTSIDE any unit of work.
/// </summary>
public interface IChargeGateway
{
  Task<ChargeOutcome> Charge(PenaltyInfo penalty, CancellationToken ct);
}

/// <summary>Port to charity.v1: the charity's display name for the notification payload, or null.</summary>
public interface ICharityNames
{
  Task<string?> NameOf(Guid charityId, CancellationToken ct);
}

/// <summary>Port to the penalty tables (implemented in data/). Every user read takes an <see cref="Owner"/>.</summary>
public interface IPenaltyStore
{
  /// <summary>Idempotent on HabitExecutionId: true when newly inserted.</summary>
  Task<bool> Enqueue(NewPenalty p, DateTime now, CancellationToken ct);

  /// <summary>Atomically claims pending (or stale processing) rows, oldest first, flipping them to processing.</summary>
  Task<IReadOnlyList<PenaltyInfo>> ClaimPending(int batchSize, DateTime staleBefore, DateTime now, CancellationToken ct);

  /// <summary>Charged + intent id + charity credit, under a row lock. Null when missing or already charged (no-op).</summary>
  Task<PenaltyInfo?> MarkCharged(Guid id, string paymentIntentId, DateTime now, CancellationToken ct);

  /// <summary>The write-backs below apply only while the row is still claimed (processing); false otherwise.</summary>
  Task<bool> MarkPending(Guid id, string? paymentIntentId, int attempts, DateTime now, CancellationToken ct);

  Task<bool> Bump(Guid id, string error, DateTime now, CancellationToken ct);

  Task<bool> MarkFailed(Guid id, string error, DateTime now, CancellationToken ct);

  Task<bool> MarkSkipped(Guid id, DateTime now, CancellationToken ct);

  Task<PenaltyInfo?> Get(Owner owner, Guid id, CancellationToken ct);

  Task<IReadOnlyList<PenaltyInfo>> Search(Owner owner, PenaltySearch search, CancellationToken ct);

  /// <summary>Sum per currency of the user's penalties that are not charged.</summary>
  Task<IReadOnlyList<(string Currency, long TotalCents, int Count)>> Unpaid(string userId, CancellationToken ct);

  Task<IReadOnlyList<CharityBalance>> CharityBalances(CancellationToken ct);

  Task<IReadOnlyList<PayoutGroup>> PayoutCandidates(long minPayoutCents, CancellationToken ct);

  Task<long> ClaimForPayout(Guid disbursementId, Guid charityId, string currency, DateTime now, CancellationToken ct);

  Task ReleasePayout(Guid disbursementId, DateTime now, CancellationToken ct);

  /// <summary>Anonymise-retain: the user's rows keep amounts, charity and intent, but lose the user id.</summary>
  Task<int> Anonymise(string userId, CancellationToken ct);
}

public static class Stake
{
  /// <summary>Exact cents (LazyTax PenaltyStakeBinding): stake * ratioBps / 10000, truncated.</summary>
  public static long Amount(long stakeCents, long ratioBps) => stakeCents * ratioBps / 10_000L;

  /// <summary>The user id that replaces a deleted user's on the retained money ledger.</summary>
  public const string DeletedUser = "deleted-user";
}
