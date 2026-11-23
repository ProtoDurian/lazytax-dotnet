using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;

namespace Ctx.Penalty.Contract.V1;

/// <summary>
/// penalty.v1: the charity stakes a user owes for missed checks, and the per-(charity, currency) accrual
/// ledger (LazyTax Domain/Penalty + Domain/Check/PenaltyStakeBinding.cs). Never rename or remove a member
/// once merged; add new ones.
/// </summary>
public interface IPenaltyV1
{
  /// <summary>
  /// What the user still owes: every penalty that is not charged yet (pending, processing, failed, skipped).
  /// Money is never summed across currencies. TotalCents 0 when nothing is owed. The delete guard uses it.
  /// </summary>
  Task<OutstandingDebt> Outstanding(string userId, CancellationToken ct = default);

  /// <summary>Every (charity, currency) accrual row: charged penalties credited to the charity.</summary>
  Task<IReadOnlyList<CharityBalance>> CharityBalances(CancellationToken ct = default);

  /// <summary>The owner's penalties, newest first. <see cref="Owner.Anyone"/> for an admin.</summary>
  Task<IReadOnlyList<PenaltyInfo>> Search(Owner owner, PenaltySearch search, CancellationToken ct = default);

  /// <summary>One penalty, or null when it does not exist or is not the owner's.</summary>
  Task<PenaltyInfo?> Get(Owner owner, Guid id, CancellationToken ct = default);

  // ── For disbursement (LazyTax's DisbursementRepository read and stamped Penalties directly). ──
  // Call these inside ONE IUnitOfWork.Start: PayoutCandidates locks the charged, unclaimed penalties
  // (FOR UPDATE SKIP LOCKED) until the unit ends, so two payout passes never claim the same one.

  /// <summary>Charged penalties not yet claimed by a payout, per (charity, currency) with sum &gt;= <paramref name="minPayoutCents"/>, largest first.</summary>
  Task<IReadOnlyList<PayoutGroup>> PayoutCandidates(long minPayoutCents, CancellationToken ct = default);

  /// <summary>Stamps the charged, unclaimed penalties of (charity, currency) with the disbursement. Returns the cents stamped (0 when none).</summary>
  Task<long> ClaimForPayout(Guid disbursementId, Guid charityId, string currency, CancellationToken ct = default);

  /// <summary>A payout failed: release its penalties back to "pending payout". Idempotent.</summary>
  Task ReleasePayout(Guid disbursementId, CancellationToken ct = default);
}

/// <summary>LazyTax PenaltyStatus, as text. "processing" = claimed by a drain pass (a lease), never charged twice.</summary>
public static class PenaltyStatus
{
  public const string Pending = "pending";
  public const string Processing = "processing";
  public const string Charged = "charged";
  public const string Failed = "failed";
  public const string Skipped = "skipped";

  public static readonly IReadOnlyList<string> All = [Pending, Processing, Charged, Failed, Skipped];
}

/// <summary>One penalty: one per missed, staked check execution (the idempotency key).</summary>
public sealed record PenaltyInfo(
  Guid Id,
  Guid HabitExecutionId,
  Guid HabitId,
  string UserId,
  Guid CharityId,
  long AmountCents,
  string Currency,
  string Status,
  string? PaymentIntentId,
  int Attempts,
  string? LastError,
  Guid? DisbursementId,
  DateTime CreatedAt,
  DateTime UpdatedAt);

/// <summary>Search filter. <paramref name="Status"/> is one of <see cref="PenaltyStatus"/>; Limit 1-100 (default 20).</summary>
public sealed record PenaltySearch(string? Status = null, int Limit = 20, int Skip = 0);

/// <summary>
/// What a user owes. <paramref name="TotalCents"/> / <paramref name="Currency"/> are the first currency owed (as
/// LazyTax's debt gate reported it); <paramref name="ByCurrency"/> has every currency. Currency "" when nothing is owed.
/// </summary>
public sealed record OutstandingDebt(long TotalCents, string Currency, IReadOnlyList<CurrencyAmount> ByCurrency, int Count);

public sealed record CurrencyAmount(string Currency, long TotalCents);

public sealed record CharityBalance(Guid CharityId, string Currency, long AccruedCents, DateTime UpdatedAt);

public sealed record PayoutGroup(Guid CharityId, string Currency, long AmountCents);

/// <summary>Outbox event names emitted by penalty.</summary>
public static class PenaltyEvents
{
  /// <summary>A penalty was charged and credited to its charity (payload <see cref="PenaltyCharged"/>). notification: email + inbox.</summary>
  public const string Charged = "penalty.charged";

  /// <summary>A penalty could not be charged and will not be retried (payload <see cref="PenaltyFailed"/>). notification: email + inbox.</summary>
  public const string Failed = "penalty.failed";
}

/// <summary>Payload of <c>penalty.charged</c>. <paramref name="CharityName"/> is null when charity did not know it.</summary>
public sealed record PenaltyCharged(
  Guid PenaltyId,
  string UserId,
  Guid HabitId,
  Guid HabitExecutionId,
  Guid CharityId,
  string? CharityName,
  long AmountCents,
  string Currency,
  string PaymentIntentId,
  DateTime ChargedAtUtc);

/// <summary>Payload of <c>penalty.failed</c>.</summary>
public sealed record PenaltyFailed(
  Guid PenaltyId,
  string UserId,
  Guid HabitId,
  Guid HabitExecutionId,
  Guid CharityId,
  string? CharityName,
  long AmountCents,
  string Currency,
  string Error,
  DateTime AttemptedAtUtc);

/// <summary>409 (as LazyTax): account deletion is blocked while the user owes money. Thrown by penalty's delete guard.</summary>
public sealed class AccountDeletionBlocked(OutstandingDebt debt)
  : Problem(409, "account_deletion_blocked", "Account Deletion Blocked",
    $"Account deletion is blocked because you have an outstanding debt of {debt.TotalCents / 100m:0.00} {debt.Currency}. " +
    "Please settle your debt before deleting your account.",
    new { totalDebt = debt.TotalCents / 100m, currency = debt.Currency, byCurrency = debt.ByCurrency })
{
  public OutstandingDebt Debt { get; } = debt;
}
