using Ctx.Check.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using LazyTax.Platform.Problems;
using Microsoft.AspNetCore.Mvc;

namespace Ctx.Check.Api.V1;

/// <summary>
/// The complete, skip, executions and overview routes of LazyTax's HabitController (same URLs), now owned by
/// check. The rest of /api/v1/Habit stays in habit.
/// </summary>
[Route("Habit")]
public sealed class CheckController(CheckService checks, OverviewService overview, ICheckStore store) : ContextController
{
  [Policy(Policies.Registered), Subject("userId")]
  [HttpPost("{userId}/{habitVersionId:guid}/executions")]
  public async Task<HabitExecutionRes> CompleteHabit(string userId, Guid habitVersionId, [FromBody] CompleteHabitReq req, CancellationToken ct)
    => (await checks.CompleteHabit(userId, habitVersionId, req.Notes, null, ct)).ToRes();

  [Policy(Policies.Registered), Subject("userId"), Guards(typeof(SkipQuotaGuard))]
  [HttpPost("{userId}/{habitVersionId:guid}/executions/skip")]
  public async Task<HabitExecutionRes> SkipHabit(string userId, Guid habitVersionId, [FromBody] SkipHabitReq req, CancellationToken ct)
    => (await checks.SkipHabit(userId, habitVersionId, req.Notes, null, ct)).ToRes();

  [Policy(Policies.Registered), Subject("userId")]
  [HttpGet("{userId}/executions")]
  public async Task<List<HabitExecutionRes>> GetDailyExecutions(string userId, [FromQuery] SearchHabitExecutionQuery query, CancellationToken ct)
    => [.. (await checks.SearchExecutions(Owner.User(userId), query.ToSearch(), ct)).Select(e => e.ToRes())];

  /// <summary>Added (LazyTax had none): one execution, owner-scoped.</summary>
  [Policy(Policies.Registered), Subject("userId")]
  [HttpGet("{userId}/executions/{executionId:guid}")]
  public async Task<HabitExecutionRes> GetExecution(string userId, Guid executionId, CancellationToken ct)
    => (await store.GetExecution(Owner.User(userId), executionId, ct) ?? throw new NotFound("HabitExecution", executionId.ToString())).ToRes();

  [Policy(Policies.Registered), Subject("userId")]
  [HttpGet("{userId}/overview")]
  public async Task<HabitOverviewResponse> GetOverview(string userId, [FromQuery] OverviewQuery query, CancellationToken ct)
  {
    var (limit, skip) = Mapper.Page(query.Limit, query.Skip, "Overview Query");
    return (await overview.Get(Owner.User(userId), limit, skip, null, ct)).ToRes();
  }
}

/// <summary>LazyTax HabitCronController: an admin-only manual tick.</summary>
[Route("habit-cron")]
public sealed class HabitCronController(CheckService checks) : ContextController
{
  [Policy(Policies.Admin)]
  [HttpPost("mark-daily-failures")]
  public async Task<MarkDailyFailuresCronRes> MarkDailyFailuresCron(CancellationToken ct)
    => new(await checks.Tick(DateTime.UtcNow, ct));
}
