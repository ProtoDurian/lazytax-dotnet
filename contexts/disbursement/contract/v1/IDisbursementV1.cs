namespace Ctx.Disbursement.Contract.V1;

/// <summary>
/// disbursement.v1: the charity payout pass (LazyTax Domain/Disbursement). It reads the charged, unclaimed
/// penalties through penalty.v1, finds each charity's Pledge organisation through charity.v1, and donates one
/// payout per (charity, currency). Payout rows belong to no user, so reads take no <c>Owner</c>; the routes
/// are admin-only. Never rename or remove a member once merged; add new ones.
/// </summary>
public interface IDisbursementV1
{
  /// <summary>
  /// Runs one payout pass: reconcile the crashed-mid-donation payouts, then claim and donate the pending ones.
  /// Returns how many payouts reached <see cref="DisbursementStatus.Completed"/> in this pass. The donation
  /// call is made outside every transaction.
  /// </summary>
  Task<int> ProcessPending(DonorIdentity donor, long minPayoutCents, int maxGroups, CancellationToken ct = default);

  /// <summary>Payouts, newest first. Admin use.</summary>
  Task<IReadOnlyList<DisbursementInfo>> Search(DisbursementSearch search, CancellationToken ct = default);

  /// <summary>One payout, or null when none has this id.</summary>
  Task<DisbursementInfo?> Get(Guid id, CancellationToken ct = default);
}

/// <summary>LazyTax DisbursementStatus, as text. "pending" = claimed, donation not yet confirmed at Pledge.</summary>
public static class DisbursementStatus
{
  public const string Pending = "pending";
  public const string Completed = "completed";
  public const string Failed = "failed";

  public static readonly IReadOnlyList<string> All = [Pending, Completed, Failed];
}

/// <summary>The legal donor of record (LazyTax), recorded on every donation.</summary>
public sealed record DonorIdentity(string FirstName, string LastName, string Email);

/// <summary>One payout to a single (charity, currency).</summary>
public sealed record DisbursementInfo(
  Guid Id,
  Guid CharityId,
  string Currency,
  long AmountCents,
  string Status,
  string PledgeOrganizationId,
  string? ProviderDonationId,
  int Attempts,
  string? LastError,
  DateTime CreatedAt,
  DateTime UpdatedAt);

/// <summary>Search filter. <paramref name="Status"/> is one of <see cref="DisbursementStatus"/>; Limit 1-100 (default 20).</summary>
public sealed record DisbursementSearch(string? Status = null, Guid? CharityId = null, int Limit = 20, int Skip = 0);
