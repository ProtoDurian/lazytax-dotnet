using Ctx.Charity.Contract.V1;
using Ctx.Charity.Domain;
using LazyTax.Platform.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ctx.Charity.Api.V1;

/// <summary>/api/v1/Charity: the catalogue. Anyone signed in reads; only admin writes. No owner: charities are global.</summary>
[Route("Charity")]
public sealed class CharityController(CharityService service) : ContextController
{
  [Policy(Policies.Registered), HttpGet("")]
  public async Task<IEnumerable<CharityPrincipalRes>> Search([FromQuery] CharitySearchReq req, CancellationToken ct) =>
    (await service.Search(req.ToQuery(), ct)).Select(c => c.ToRes());

  [Policy(Policies.Registered), HttpGet("{id:guid}")]
  public async Task<CharityRes> GetById(Guid id, CancellationToken ct) => new((await service.Get(id, ct)).ToRes());

  [Policy(Policies.Registered), HttpGet("supported-countries")]
  public IEnumerable<string> GetSupportedCountries() => PledgeSyncService.SupportedCountries;

  [Policy(Policies.Admin), HttpPut("{id:guid}/causes")]
  public async Task<IActionResult> SetCauses(Guid id, [FromBody] SetCharityCausesReq req, CancellationToken ct)
  {
    await service.SetCauses(id, req.Keys, ct);
    return this.NoContent();
  }

  [Policy(Policies.Admin), HttpPost("")]
  public async Task<CharityPrincipalRes> Create([FromBody] CreateCharityReq req, CancellationToken ct) =>
    (await service.Create(req.ToRecord(), ct)).ToRes();

  [Policy(Policies.Admin), HttpPut("{id:guid}")]
  public async Task<CharityPrincipalRes> Update(Guid id, [FromBody] UpdateCharityReq req, CancellationToken ct) =>
    (await service.Update(id, req.ToRecord(), ct)).ToRes();

  [Policy(Policies.Admin), HttpDelete("{id:guid}")]
  public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
  {
    await service.Delete(id, ct);
    return this.NoContent();
  }
}
