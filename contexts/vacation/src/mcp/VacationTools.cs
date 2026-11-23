using Ctx.Vacation.Api.V1;
using Ctx.Vacation.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using LazyTax.Platform.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Vacation.Mcp;

public static class VacationTools
{
  public sealed record CreateArgs(string UserId, string StartDate, string EndDate, string Timezone);

  /// <summary>Same as POST /api/v1/Vacation/{userId}: same policy, subject, guard and service method.</summary>
  public static readonly McpToolDefinition Create = McpTool.Define<CreateArgs>(
    "vacation.create", "Create a vacation period (dates dd-MM-yyyy, user-local)", Policies.Registered,
    async call => (object?)(await call.Services.GetRequiredService<VacationService>()
      .Create(call.Args.UserId, new CreateVacationReq(call.Args.StartDate, call.Args.EndDate, call.Args.Timezone).ToRecord(), call.Ct)).ToRes(),
    subject: "userId",
    typeof(VacationQuotaGuard));

  public sealed record ListArgs(string UserId, int? Year);

  /// <summary>Same as GET /api/v1/Vacation/{userId}.</summary>
  public static readonly McpToolDefinition List = McpTool.Define<ListArgs>(
    "vacation.list", "List a user's vacation periods, newest first", Policies.Registered,
    async call => (object?)(await call.Services.GetRequiredService<VacationService>()
      .Search(Owner.User(call.Args.UserId), new SearchVacationQuery(call.Args.Year, null, null).ToDomain(), call.Ct)).Select(v => v.ToRes()).ToList(),
    subject: "userId");
}
