using Ctx.Disbursement.Contract.V1;
using Ctx.Disbursement.Domain;

namespace Ctx.Disbursement.Data;

/// <summary>disbursement.v1.</summary>
public sealed class DisbursementV1(DisbursementService service, IDisbursementRepository repo) : IDisbursementV1
{
  public Task<int> ProcessPending(DonorIdentity donor, long minPayoutCents, int maxGroups, CancellationToken ct = default) =>
    service.ProcessPending(donor, minPayoutCents, maxGroups, ct);

  public Task<IReadOnlyList<DisbursementInfo>> Search(DisbursementSearch search, CancellationToken ct = default) => repo.Search(search, ct);

  public Task<DisbursementInfo?> Get(Guid id, CancellationToken ct = default) => repo.Get(id, ct);
}
