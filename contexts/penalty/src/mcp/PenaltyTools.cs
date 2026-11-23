using Ctx.Penalty.Api.V1;
using Ctx.Penalty.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using LazyTax.Platform.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Penalty.Mcp;

/// <summary>penalty.* MCP tools: the same policy, subject and service method as the routes.</summary>
public static class PenaltyTools
{
  public sealed record SearchArgs(string UserId, string? Status, int? Limit, int? Skip);

  public sealed record GetArgs(string UserId, Guid Id);

  public sealed record UserArgs(string UserId);

  public sealed record NoArgs;

  private static PenaltyService Svc(IServiceProvider sp) => sp.GetRequiredService<PenaltyService>();

  public static readonly McpToolDefinition Search = McpTool.Define<SearchArgs>(
    "penalty.search", "List a user's penalties (missed staked checks), newest first", Policies.Registered,
    async call => await Svc(call.Services).Search(Owner.User(call.Args.UserId),
      PenaltyController.Query(call.Args.Status, call.Args.Limit, call.Args.Skip), call.Ct),
    subject: "userId");

  public static readonly McpToolDefinition Get = McpTool.Define<GetArgs>(
    "penalty.get", "Get one of a user's penalties by id", Policies.Registered,
    async call => await Svc(call.Services).GetOwned(Owner.User(call.Args.UserId), call.Args.Id, call.Ct),
    subject: "userId");

  public static readonly McpToolDefinition Outstanding = McpTool.Define<UserArgs>(
    "penalty.outstanding", "What a user still owes: penalties not charged yet, per currency", Policies.Registered,
    async call => await Svc(call.Services).Outstanding(call.Args.UserId, call.Ct),
    subject: "userId");

  public static readonly McpToolDefinition CharityBalances = McpTool.Define<NoArgs>(
    "penalty.charityBalances", "Admin: the amount accrued per charity and currency from charged penalties", Policies.Admin,
    async call => await Svc(call.Services).CharityBalances(call.Ct));
}
