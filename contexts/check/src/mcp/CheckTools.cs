using Ctx.Check.Api.V1;
using Ctx.Check.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using LazyTax.Platform.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Check.Mcp;

/// <summary>MCP tools: the same policy, subject rule, guards and service methods as the routes.</summary>
public static class CheckTools
{
  public sealed record HabitVersionArgs(string UserId, Guid HabitVersionId, string? Notes);

  public sealed record OverviewArgs(string UserId, int? Limit, int? Skip);

  public sealed record ExecutionsArgs(string UserId, Guid? HabitId, string? Date, int? Limit, int? Skip);

  public static readonly McpToolDefinition Complete = McpTool.Define<HabitVersionArgs>(
    "check.complete", "Complete today's check of a habit (manual proof)", Policies.Registered,
    async call => (await call.Services.GetRequiredService<CheckService>()
      .CompleteHabit(call.Args.UserId, call.Args.HabitVersionId, call.Args.Notes, null, call.Ct)).ToRes(),
    subject: "userId");

  public static readonly McpToolDefinition Skip = McpTool.Define<HabitVersionArgs>(
    "check.skip", "Skip today's check of a habit, within the month's skips", Policies.Registered,
    async call => (await call.Services.GetRequiredService<CheckService>()
      .SkipHabit(call.Args.UserId, call.Args.HabitVersionId, call.Args.Notes, null, call.Ct)).ToRes(),
    "userId", typeof(SkipQuotaGuard));

  public static readonly McpToolDefinition Overview = McpTool.Define<OverviewArgs>(
    "check.overview", "The user's habits with streaks, this week, today's deadline and debt", Policies.Registered,
    async call =>
    {
      var (limit, skip) = Mapper.Page(call.Args.Limit, call.Args.Skip, "Overview Query");
      return (await call.Services.GetRequiredService<OverviewService>()
        .Get(Owner.User(call.Args.UserId), limit, skip, null, call.Ct)).ToRes();
    },
    subject: "userId");

  public static readonly McpToolDefinition Executions = McpTool.Define<ExecutionsArgs>(
    "check.executions", "Search the user's habit executions", Policies.Registered,
    async call =>
    {
      var a = call.Args;
      var search = new SearchHabitExecutionQuery(a.HabitId, a.Date, a.Limit, a.Skip).ToSearch();
      return (await call.Services.GetRequiredService<CheckService>().SearchExecutions(Owner.User(a.UserId), search, call.Ct))
        .Select(e => e.ToRes()).ToList();
    },
    subject: "userId");
}
