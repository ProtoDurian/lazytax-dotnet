using Ctx.Vacation.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ctx.Vacation.Api.V1;

/// <summary>LazyTax's VacationController, at the same paths (/api/v1/Vacation/...).</summary>
[Route("")]
public sealed class VacationController(VacationService service) : ContextController
{
  [Policy(Policies.Registered), Subject("userId"), Guards(typeof(VacationQuotaGuard))]
  [HttpPost("{userId}")]
  public async Task<VacationRes> Create(string userId, [FromBody] CreateVacationReq req, CancellationToken ct) =>
    (await service.Create(userId, req.ToRecord(), ct)).ToRes();

  [Policy(Policies.Registered), Subject("userId"), HttpGet("{userId}")]
  public async Task<IEnumerable<VacationRes>> List(string userId, [FromQuery] SearchVacationQuery query, CancellationToken ct) =>
    (await service.Search(Owner.User(userId), query.ToDomain(), ct)).Select(v => v.ToRes());

  [Policy(Policies.Registered), Subject("userId"), HttpDelete("{userId}/{id:guid}")]
  public async Task<IActionResult> Delete(string userId, Guid id, CancellationToken ct)
  {
    await service.Delete(userId, id, ct);
    return this.NoContent();
  }

  [Policy(Policies.Registered), Subject("userId"), HttpPatch("{userId}/{id:guid}/end-today")]
  public async Task<VacationRes> EndToday(string userId, Guid id, CancellationToken ct) =>
    (await service.EndToday(userId, id, ct)).ToRes();
}
