using Ctx.Account.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ctx.Account.Api.V1;

/// <summary>LazyTax's ConfigurationController, at the same paths (/api/v1/Configuration/...).</summary>
[Route("Configuration")]
public sealed class ConfigurationController(ConfigurationService service) : ContextController
{
  [Policy(Policies.Registered), Subject("userId"), HttpGet("{userId}/{id:guid}")]
  public async Task<ConfigurationRes> GetByUserAndId(string userId, Guid id, CancellationToken ct) =>
    new((await service.Get(Owner.User(userId), id, ct)).ToRes(), null);

  [Policy(Policies.Registered), HttpGet("me")]
  public async Task<ConfigurationRes> GetMine(CancellationToken ct) =>
    new((await service.GetByUser(this.Caller.RequireSub(), ct)).ToRes(), null);

  [Policy(Policies.Registered), HttpGet("{id:guid}")]
  public async Task<ConfigurationRes> GetById(Guid id, CancellationToken ct) =>
    new((await service.Get(this.Self(), id, ct)).ToRes(), null);

  [Policy(Policies.Registered), Subject("userId"), HttpPost("{userId}")]
  public async Task<ConfigurationPrincipalRes> CreateForUser(string userId, [FromBody] CreateConfigurationReq req, CancellationToken ct) =>
    (await service.Create(userId, Mapper.ToRecord(req.Timezone, req.DefaultCharityId), ct)).ToRes();

  [Policy(Policies.Registered), HttpPost]
  public async Task<ConfigurationPrincipalRes> Create([FromBody] CreateConfigurationReq req, CancellationToken ct) =>
    (await service.Create(this.Caller.RequireSub(), Mapper.ToRecord(req.Timezone, req.DefaultCharityId), ct)).ToRes();

  [Policy(Policies.Registered), Subject("userId"), HttpPut("{userId}/{id:guid}")]
  public Task<UpdateConfigurationRes> UpdateForUser(string userId, Guid id, [FromBody] UpdateConfigurationReq req, CancellationToken ct) =>
    this.Update(Owner.User(userId), id, req, ct);

  [Policy(Policies.Registered), HttpPut("{id:guid}")]
  public Task<UpdateConfigurationRes> Update(Guid id, [FromBody] UpdateConfigurationReq req, CancellationToken ct) =>
    this.Update(Owner.User(this.Caller.RequireSub()), id, req, ct);

  private async Task<UpdateConfigurationRes> Update(Owner owner, Guid id, UpdateConfigurationReq req, CancellationToken ct)
  {
    var c = await service.Update(owner, id, Mapper.ToRecord(req.Timezone, req.DefaultCharityId), ct);
    // Settled checks now come from check's subscriber to account.timezone_changed, after commit.
    return new UpdateConfigurationRes(c.Id.ToString(), c.UserId, c.Record.Timezone, c.Record.DefaultCharityId?.ToString(), []);
  }

  private Owner Self() => this.Caller.IsAdmin ? Owner.Anyone() : Owner.User(this.Caller.RequireSub());
}
