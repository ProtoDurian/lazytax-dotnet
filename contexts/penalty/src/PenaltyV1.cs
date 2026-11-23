using Ctx.Penalty.Contract.V1;
using Ctx.Penalty.Domain;
using LazyTax.Platform.Data;

namespace Ctx.Penalty;

/// <summary>penalty.v1, over the service.</summary>
internal sealed class PenaltyV1(PenaltyService service) : IPenaltyV1
{
  public Task<OutstandingDebt> Outstanding(string userId, CancellationToken ct = default) => service.Outstanding(userId, ct);
  public Task<IReadOnlyList<CharityBalance>> CharityBalances(CancellationToken ct = default) => service.CharityBalances(ct);
  public Task<IReadOnlyList<PenaltyInfo>> Search(Owner owner, PenaltySearch search, CancellationToken ct = default) => service.Search(owner, search, ct);
  public Task<PenaltyInfo?> Get(Owner owner, Guid id, CancellationToken ct = default) => service.Get(owner, id, ct);
  public Task<IReadOnlyList<PayoutGroup>> PayoutCandidates(long minPayoutCents, CancellationToken ct = default) => service.PayoutCandidates(minPayoutCents, ct);
  public Task<long> ClaimForPayout(Guid disbursementId, Guid charityId, string currency, CancellationToken ct = default) =>
    service.ClaimForPayout(disbursementId, charityId, currency, ct);
  public Task ReleasePayout(Guid disbursementId, CancellationToken ct = default) => service.ReleasePayout(disbursementId, ct);
}
