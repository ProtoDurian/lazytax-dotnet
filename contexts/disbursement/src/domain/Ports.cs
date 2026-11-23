using Ctx.Disbursement.Contract.V1;

namespace Ctx.Disbursement.Domain;

/// <summary>A donation request to the vendor (Pledge). Amount in minor units; the adapter formats it.</summary>
public sealed record DonationRequest(
  string OrganizationId,
  long AmountCents,
  string Currency,
  string Reference,
  string DonorFirstName,
  string DonorLastName,
  string DonorEmail);

/// <summary>A donation at the vendor.</summary>
public sealed record DonationResult(string DonationId, string Status);

/// <summary>
/// Signals that the donation provider DEFINITIVELY refused the request (a 4xx), so no donation was created. Only then
/// is it safe to release the penalties for a fresh retry. Any other failure is AMBIGUOUS (the donation may have
/// landed) and must leave the payout pending for reconciliation, or a release risks a double payout.
/// </summary>
public sealed class DonationRejectedException(string? message) : Exception(message);

/// <summary>Domain port over the donation vendor. The only implementation is a fake: no network.</summary>
public interface IDonationGateway
{
  /// <summary>Creates a donation. <c>Reference</c> (our payout id) is stored at the vendor for reconciliation.</summary>
  Task<DonationResult> CreateDonation(DonationRequest request, CancellationToken ct = default);

  /// <summary>
  /// The donation made for <paramref name="reference"/>; null ONLY when the lookup conclusively found none.
  /// Throws when the lookup is inconclusive, so the caller never releases on a guess.
  /// </summary>
  Task<DonationResult?> FindDonationByReference(string reference, CancellationToken ct = default);
}

/// <summary>Where a charity is paid: its organisation id at the donation vendor. Backed by charity.v1.</summary>
public interface IPayoutTargets
{
  /// <summary>The Pledge organisation id of the charity, or null when it has none (it is then skipped, not failed).</summary>
  Task<string?> PledgeOrganizationId(Guid charityId, CancellationToken ct = default);
}

/// <summary>Owns the <c>disbursement.Disbursements</c> rows.</summary>
public interface IDisbursementRepository
{
  Task Insert(DisbursementInfo row, CancellationToken ct = default);

  /// <summary>Row-locks the payout, then sets it Completed. False when it is missing or already Completed (a no-op).</summary>
  Task<bool> MarkCompleted(Guid id, string providerDonationId, CancellationToken ct = default);

  /// <summary>Row-locks the payout, then sets it Failed and bumps Attempts. False when it is missing or already Completed.</summary>
  Task<bool> MarkFailed(Guid id, string error, CancellationToken ct = default);

  /// <summary>Pending payouts last touched before <paramref name="cutoffUtc"/>, oldest first.</summary>
  Task<IReadOnlyList<DisbursementInfo>> GetReconcilable(DateTime cutoffUtc, int batchSize, CancellationToken ct = default);

  Task<DisbursementInfo?> Get(Guid id, CancellationToken ct = default);

  Task<IReadOnlyList<DisbursementInfo>> Search(DisbursementSearch search, CancellationToken ct = default);
}
