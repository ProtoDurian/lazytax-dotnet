using Ctx.Habit.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using LazyTax.Platform.Problems;
using Microsoft.AspNetCore.Mvc;

namespace Ctx.Habit.Api.V1;

[Route("Habit")]
public sealed class HabitController(HabitService service) : ContextController
{
  [Policy(Policies.Registered), Subject("userId")]
  [HttpGet("{userId}")]
  public async Task<List<HabitVersionRes>> Search(string userId, [FromQuery] SearchHabitQuery query, CancellationToken ct)
  {
    var (limit, skip) = HabitRules.Page(query.Limit, query.Skip);
    if (query.Task is { Length: > 256 }) throw new ValidationFailed("Invalid Search Habit Query", "Task", "Task must be 256 characters or less.");
    var found = await service.Search(Owner.User(userId), new HabitSearch(query.Id, query.Task, query.Enabled, limit, skip), ct);
    return found.Select(h => h.ToRes()).ToList();
  }

  [Policy(Policies.Registered), Subject("userId")]
  [HttpGet("{userId}/{id:guid}")]
  public async Task<HabitVersionRes> Get(string userId, Guid id, CancellationToken ct) =>
    (await service.GetCurrent(Owner.User(userId), id, ct)).ToRes();

  [Policy(Policies.Registered), Subject("userId"), Guards(typeof(HabitQuotaGuard))]
  [HttpPost("{userId}")]
  public async Task<HabitVersionRes> Create(string userId, [FromBody] CreateHabitReq req, CancellationToken ct) =>
    (await service.Create(userId, req.ToInput(), req.RoutineId, ct)).ToRes();

  [Policy(Policies.Registered), Subject("userId")]
  [HttpPut("{userId}/{id:guid}")]
  public async Task<HabitVersionRes> Update(string userId, Guid id, [FromBody] UpdateHabitReq req, CancellationToken ct) =>
    (await service.Update(userId, id, req.ToInput(), req.Enabled, ct)).ToRes();

  // Saves the user's habit order: the listed habits first, then the rest in their current order.
  [Policy(Policies.Registered), Subject("userId")]
  [HttpPut("{userId}/order")]
  public async Task<ReorderHabitsRes> Reorder(string userId, [FromBody] ReorderHabitsReq req, CancellationToken ct)
  {
    var order = await service.Reorder(userId, HabitMapper.ParseIds(req.HabitIds), ct);
    return new ReorderHabitsRes(order.Select(i => i.ToString()).ToArray());
  }

  [Policy(Policies.Registered), Subject("userId")]
  [HttpDelete("{userId}/{id:guid}")]
  public async Task<IActionResult> Delete(string userId, Guid id, CancellationToken ct)
  {
    await service.Delete(userId, id, ct);
    return this.NoContent();
  }
}
