using Ctx.Protection.Api.V1;
using Ctx.Protection.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using LazyTax.Platform.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Protection.Mcp;

public static class ProtectionTools
{
  public sealed record UserArgs(string UserId);

  public static readonly McpToolDefinition Balance = McpTool.Define<UserArgs>(
    "protection.balance", "A user's banked freeze days and their cap", Policies.Registered,
    async call =>
    {
      var b = await call.Services.GetRequiredService<ProtectionService>().Balance(Owner.User(call.Args.UserId), call.Ct);
      return new ProtectionBalanceRes(b.UserId, b.Balance, b.Cap);
    },
    subject: "userId");

  public static readonly McpToolDefinition AwardWeekly = McpTool.Define<UserArgs>(
    "protection.awardWeekly", "Award a user's weekly freezes for last week's perfect habits (admin)", Policies.Admin,
    async call => new AwardWeeklyRes(call.Args.UserId,
      await call.Services.GetRequiredService<ProtectionService>().AwardWeekly(call.Args.UserId, null, call.Ct)));
}
