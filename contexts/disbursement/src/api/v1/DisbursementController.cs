using Ctx.Disbursement.Contract.V1;
using Ctx.Disbursement.Domain;
using LazyTax.Platform.Http;
using LazyTax.Platform.Problems;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Ctx.Disbursement.Api.V1;

public sealed record RunDisbursementRes(int Completed);

/// <summary>
/// Admin-only. POST run: one payout pass on demand (reconcile + claim + donate) with the configured donor and
/// thresholds, as LazyTax's <c>POST api/v1/disbursement/run</c>. GET lists and reads payouts (new).
/// </summary>
[Route("")]
[Policy(Policies.Admin)]
public sealed class DisbursementController(IDisbursementV1 disbursement, IOptions<DisbursementOptions> options) : ContextController
{
  [HttpPost("run")]
  public async Task<RunDisbursementRes> Run(CancellationToken ct)
  {
    var o = options.Value;
    return new RunDisbursementRes(await disbursement.ProcessPending(
      new DonorIdentity(o.DonorFirstName, o.DonorLastName, o.DonorEmail), o.MinPayoutCents, o.MaxGroupsPerRun, ct));
  }

  [HttpGet("")]
  public Task<IReadOnlyList<DisbursementInfo>> Search(
    [FromQuery] string? status, [FromQuery] Guid? charityId, [FromQuery] int limit = 20, [FromQuery] int skip = 0, CancellationToken ct = default)
  {
    if (status is not null && !DisbursementStatus.All.Contains(status))
      throw new ValidationFailed("Invalid status", "status", $"one of {string.Join(", ", DisbursementStatus.All)}");
    return disbursement.Search(new DisbursementSearch(status, charityId, limit, skip), ct);
  }

  [HttpGet("{id:guid}")]
  public async Task<DisbursementInfo> Get(Guid id, CancellationToken ct) =>
    await disbursement.Get(id, ct) ?? throw new NotFound("Disbursement", id.ToString());
}
