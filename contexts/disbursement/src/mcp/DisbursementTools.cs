using Ctx.Disbursement.Api.V1;
using Ctx.Disbursement.Contract.V1;
using Ctx.Disbursement.Domain;
using LazyTax.Platform.Http;
using LazyTax.Platform.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ctx.Disbursement.Mcp;

public static class DisbursementTools
{
  public sealed record NoArgs;

  public sealed record ListArgs(string? Status, string? CharityId, int? Limit);

  /// <summary>Same call as <c>POST /api/v1/disbursement/run</c>.</summary>
  public static readonly McpToolDefinition Run = McpTool.Define<NoArgs>(
    "disbursement.run", "Run one charity payout pass now (reconcile, claim, donate). Admin only.", Policies.Admin,
    async call =>
    {
      var o = call.Services.GetRequiredService<IOptions<DisbursementOptions>>().Value;
      return new RunDisbursementRes(await call.Services.GetRequiredService<IDisbursementV1>().ProcessPending(
        new DonorIdentity(o.DonorFirstName, o.DonorLastName, o.DonorEmail), o.MinPayoutCents, o.MaxGroupsPerRun, call.Ct));
    });

  public static readonly McpToolDefinition List = McpTool.Define<ListArgs>(
    "disbursement.list", "List charity payouts, newest first. Admin only.", Policies.Admin,
    async call => await call.Services.GetRequiredService<IDisbursementV1>().Search(
      new DisbursementSearch(call.Args.Status, Guid.TryParse(call.Args.CharityId, out var c) ? c : null, call.Args.Limit ?? 20), call.Ct));
}
