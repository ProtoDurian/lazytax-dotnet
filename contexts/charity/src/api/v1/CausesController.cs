using Ctx.Charity.Domain;
using LazyTax.Platform.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ctx.Charity.Api.V1;

/// <summary>/api/v1/Causes. Anyone signed in reads; only admin writes.</summary>
[Route("Causes")]
public sealed class CausesController(CauseService service) : ContextController
{
  [Policy(Policies.Registered), HttpGet("")]
  public async Task<IEnumerable<CausePrincipalRes>> Search([FromQuery] CauseSearchReq req, CancellationToken ct) =>
    (await service.Search(req.ToSearch(), ct)).Select(c => c.ToRes());

  [Policy(Policies.Registered), HttpGet("{id:guid}")]
  public async Task<CauseRes> Get(Guid id, CancellationToken ct) => new((await service.Get(id, ct)).ToRes());

  [Policy(Policies.Admin), HttpPost("")]
  public async Task<CausePrincipalRes> Create([FromBody] CreateCauseReq req, CancellationToken ct) =>
    (await service.Create(new CauseRecord(req.Key, req.Name), ct)).ToRes();

  [Policy(Policies.Admin), HttpPut("{id:guid}")]
  public async Task<CausePrincipalRes> Update(Guid id, [FromBody] UpdateCauseReq req, CancellationToken ct) =>
    (await service.Update(id, new CauseRecord(string.Empty, req.Name), ct)).ToRes();

  [Policy(Policies.Admin), HttpDelete("{id:guid}")]
  public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
  {
    await service.Delete(id, ct);
    return this.NoContent();
  }
}
