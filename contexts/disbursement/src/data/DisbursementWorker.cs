using Ctx.Disbursement.Contract.V1;
using Ctx.Disbursement.Domain;
using LazyTax.Platform.Workers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ctx.Disbursement.Data;

/// <summary>
/// The daily charity payout (LazyTax <c>DisbursementHostedService</c>): reconcile in-flight payouts, then claim
/// and donate. Idempotent: the claim stamps the penalties under a row lock, so replicas never pay twice.
/// </summary>
public sealed class DisbursementWorker(
  DisbursementService service,
  IOptions<DisbursementOptions> options,
  ILogger<DisbursementWorker> logger) : IContextWorker
{
  public TimeSpan Interval => TimeSpan.FromHours(Math.Max(1, options.Value.IntervalHours));

  public async Task Tick(CancellationToken ct)
  {
    var o = options.Value;
    if (!o.Enabled)
    {
      logger.LogInformation("Disbursement worker disabled (Contexts:disbursement:Enabled=false); skipping tick");
      return;
    }
    var n = await service.ProcessPending(new DonorIdentity(o.DonorFirstName, o.DonorLastName, o.DonorEmail),
      o.MinPayoutCents, o.MaxGroupsPerRun, ct);
    logger.LogInformation("Disbursement paid out {Count} group(s)", n);
  }
}
