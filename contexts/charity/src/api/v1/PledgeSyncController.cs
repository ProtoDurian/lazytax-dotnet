using Ctx.Charity.Domain;
using LazyTax.Platform.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ctx.Charity.Api.V1;

/// <summary>/api/v1/charities/sync/pledge: admin-only catalogue sync from Pledge (a fake gateway here).</summary>
[Route("charities/sync")]
public sealed class PledgeSyncController(PledgeSyncService sync) : ContextController
{
  [Policy(Policies.Admin), HttpPost("pledge")]
  public async Task<PledgeSyncSummaryRes> Sync(CancellationToken ct)
  {
    var r = await sync.Sync(ct);
    return new PledgeSyncSummaryRes(r.CausesUpserted, r.CharitiesCreated, r.CharitiesUpdated, r.ExternalIdsLinked, r.CharitiesProcessed);
  }
}
