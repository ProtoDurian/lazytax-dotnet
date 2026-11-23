using Ctx.Habit.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ctx.Habit.Api.V1;

[Route("Routine")]
public sealed class RoutineController(RoutineService service) : ContextController
{
  [Policy(Policies.Registered), Subject("userId")]
  [HttpGet("{userId}")]
  public async Task<List<RoutineRes>> Search(string userId, [FromQuery] SearchRoutineQuery query, CancellationToken ct)
  {
    var (limit, skip) = HabitRules.Page(query.Limit, query.Skip);
    var found = await service.Search(Owner.User(userId), new RoutineSearch(query.Name, limit, skip), ct);
    return found.Select(r => r.ToRes()).ToList();
  }

  [Policy(Policies.Registered), Subject("userId")]
  [HttpGet("{userId}/{id:guid}")]
  public async Task<RoutineRes> Get(string userId, Guid id, CancellationToken ct) =>
    (await service.Get(Owner.User(userId), id, ct)).ToRes();

  [Policy(Policies.Registered), Subject("userId")]
  [HttpPost("{userId}")]
  public async Task<RoutineRes> Create(string userId, [FromBody] CreateRoutineReq req, CancellationToken ct) =>
    (await service.Create(userId, req.Name, ct)).ToRes();

  [Policy(Policies.Registered), Subject("userId")]
  [HttpPut("{userId}/{id:guid}")]
  public async Task<RoutineRes> Update(string userId, Guid id, [FromBody] UpdateRoutineReq req, CancellationToken ct) =>
    (await service.Update(userId, id, req.Name, ct)).ToRes();

  [Policy(Policies.Registered), Subject("userId")]
  [HttpDelete("{userId}/{id:guid}")]
  public async Task<IActionResult> Delete(string userId, Guid id, CancellationToken ct)
  {
    await service.Delete(userId, id, ct);
    return this.NoContent();
  }
}
