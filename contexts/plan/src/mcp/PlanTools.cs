using Ctx.Plan.Api.V1;
using Ctx.Plan.Domain;
using LazyTax.Platform.Http;
using LazyTax.Platform.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Plan.Mcp;

public static class PlanTools
{
  public sealed record NoArgs;

  /// <summary>Same as GET /api/v1/Subscription/plans.</summary>
  public static readonly McpToolDefinition ListPlans = McpTool.Define<NoArgs>(
    "plan.list_plans", "List the plan catalog, cheapest first", Policies.Public,
    call => Task.FromResult<object?>(call.Services.GetRequiredService<Catalog>().All().Select(p => p.ToRes()).ToList()));

  public sealed record GetSubscriptionArgs(string UserId);

  /// <summary>Same as GET /api/v1/Subscription/{userId}.</summary>
  public static readonly McpToolDefinition GetSubscription = McpTool.Define<GetSubscriptionArgs>(
    "plan.get_subscription", "Get a user's effective subscription", Policies.Registered,
    async call => (object?)(await call.Services.GetRequiredService<PlanService>().Effective(call.Args.UserId, call.Ct)).ToRes(call.Args.UserId),
    subject: "userId");
}
