using Ctx.Habit.Api.V1;
using Ctx.Habit.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using LazyTax.Platform.Mcp;
using LazyTax.Platform.Problems;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Habit.Mcp;

/// <summary>MCP tools; each calls the same service method as its route, with the same policy, subject and guards.</summary>
public static class HabitTools
{
  public sealed record ListArgs(string UserId, string? Task, bool? Enabled, int? Limit, int? Skip);
  public sealed record GetArgs(string UserId, string HabitId);
  public sealed record CreateArgs(string UserId, string Task, string[] DaysOfWeek, string NotificationTime, string Stake,
    string? CharityId, string Timezone, string? DeadlineTime, string? RoutineId);
  public sealed record UpdateArgs(string UserId, string HabitId, string Task, string[] DaysOfWeek, string NotificationTime,
    string Stake, string? CharityId, bool Enabled, string Timezone, string? DeadlineTime);
  public sealed record ReorderArgs(string UserId, string[] HabitIds);
  public sealed record RoutineListArgs(string UserId, string? Name, int? Limit, int? Skip);
  public sealed record RoutineCreateArgs(string UserId, string Name);
  public sealed record RoutineDeleteArgs(string UserId, string RoutineId);

  private static Guid Id(string field, string? value) =>
    Guid.TryParse(value, out var g) ? g : throw new ValidationFailed("Invalid argument", field, $"{field} must be a GUID.");

  private static Guid? OptId(string field, string? value) => string.IsNullOrEmpty(value) ? null : Id(field, value);

  public static readonly McpToolDefinition List = McpTool.Define<ListArgs>(
    "habit.list", "List a user's habits (current versions, in the user's order)", Policies.Registered,
    async call =>
    {
      var (limit, skip) = HabitRules.Page(call.Args.Limit, call.Args.Skip);
      var found = await call.Services.GetRequiredService<HabitService>()
        .Search(Owner.User(call.Args.UserId), new HabitSearch(null, call.Args.Task, call.Args.Enabled, limit, skip), call.Ct);
      return found.Select(h => h.ToRes()).ToList();
    },
    subject: "userId");

  public static readonly McpToolDefinition Get = McpTool.Define<GetArgs>(
    "habit.get", "Get one habit's current version", Policies.Registered,
    async call => (await call.Services.GetRequiredService<HabitService>()
      .GetCurrent(Owner.User(call.Args.UserId), Id("habitId", call.Args.HabitId), call.Ct)).ToRes(),
    subject: "userId");

  public static readonly McpToolDefinition Create = McpTool.Define<CreateArgs>(
    "habit.create", "Create a habit", Policies.Registered,
    async call =>
    {
      var a = call.Args;
      var input = new HabitInput(a.Task, a.DaysOfWeek, a.NotificationTime, a.Stake, OptId("charityId", a.CharityId), a.Timezone, a.DeadlineTime);
      return (await call.Services.GetRequiredService<HabitService>()
        .Create(a.UserId, input, OptId("routineId", a.RoutineId), call.Ct)).ToRes();
    },
    subject: "userId",
    typeof(HabitQuotaGuard));

  public static readonly McpToolDefinition Update = McpTool.Define<UpdateArgs>(
    "habit.update", "Update a habit (creates a new version)", Policies.Registered,
    async call =>
    {
      var a = call.Args;
      var input = new HabitInput(a.Task, a.DaysOfWeek, a.NotificationTime, a.Stake, OptId("charityId", a.CharityId), a.Timezone, a.DeadlineTime);
      return (await call.Services.GetRequiredService<HabitService>()
        .Update(a.UserId, Id("habitId", a.HabitId), input, a.Enabled, call.Ct)).ToRes();
    },
    subject: "userId");

  public static readonly McpToolDefinition Delete = McpTool.Define<GetArgs>(
    "habit.delete", "Delete a habit", Policies.Registered,
    async call =>
    {
      await call.Services.GetRequiredService<HabitService>().Delete(call.Args.UserId, Id("habitId", call.Args.HabitId), call.Ct);
      return new { deleted = true };
    },
    subject: "userId");

  public static readonly McpToolDefinition Reorder = McpTool.Define<ReorderArgs>(
    "habit.reorder", "Put the listed habits first, in order", Policies.Registered,
    async call =>
    {
      var order = await call.Services.GetRequiredService<HabitService>()
        .Reorder(call.Args.UserId, HabitMapper.ParseIds(call.Args.HabitIds), call.Ct);
      return new ReorderHabitsRes(order.Select(i => i.ToString()).ToArray());
    },
    subject: "userId");

  public static readonly McpToolDefinition RoutineList = McpTool.Define<RoutineListArgs>(
    "habit.routine_list", "List a user's routines", Policies.Registered,
    async call =>
    {
      var (limit, skip) = HabitRules.Page(call.Args.Limit, call.Args.Skip);
      var found = await call.Services.GetRequiredService<RoutineService>()
        .Search(Owner.User(call.Args.UserId), new RoutineSearch(call.Args.Name, limit, skip), call.Ct);
      return found.Select(r => r.ToRes()).ToList();
    },
    subject: "userId");

  public static readonly McpToolDefinition RoutineCreate = McpTool.Define<RoutineCreateArgs>(
    "habit.routine_create", "Create a routine", Policies.Registered,
    async call => (await call.Services.GetRequiredService<RoutineService>().Create(call.Args.UserId, call.Args.Name, call.Ct)).ToRes(),
    subject: "userId");

  public static readonly McpToolDefinition RoutineDelete = McpTool.Define<RoutineDeleteArgs>(
    "habit.routine_delete", "Delete a routine and its habits", Policies.Registered,
    async call =>
    {
      await call.Services.GetRequiredService<RoutineService>().Delete(call.Args.UserId, Id("routineId", call.Args.RoutineId), call.Ct);
      return new { deleted = true };
    },
    subject: "userId");
}
