using Ctx.Account.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using LazyTax.Platform.Problems;
using Microsoft.AspNetCore.Mvc;

namespace Ctx.Account.Api.V1;

/// <summary>LazyTax's UserController, at the same paths (/api/v1/User/...).</summary>
[Route("User")]
public sealed class UserController(UserService service, TokenDataExtractor tokens) : ContextController
{
  [Policy(Policies.Admin), HttpGet]
  public async Task<IEnumerable<UserPrincipalRes>> Search([FromQuery] SearchUserQuery query, CancellationToken ct) =>
    (await service.Search(query.ToDomain(), ct)).Select(u => u.ToRes());

  [Policy(Policies.Registered), HttpGet("Me")]
  [Produces("text/plain")]
  public string Me() => this.Caller.Sub ?? "none";

  [Policy(Policies.Registered), HttpGet("Me/All")]
  public async Task<UserRes> MeAll(CancellationToken ct)
  {
    var sub = this.Caller.RequireSub();
    return new UserRes((await service.Get(Owner.User(sub), sub, ct)).ToRes());
  }

  [Policy(Policies.Registered), Subject("id"), HttpGet("{id}")]
  public async Task<UserRes> GetById(string id, CancellationToken ct) =>
    new((await service.Get(Owner.User(id), id, ct)).ToRes());

  [Policy(Policies.Registered), HttpGet("username/{username}")]
  public async Task<UserRes> GetByUsername(string username, CancellationToken ct)
  {
    // Owner-scoped first read: a user finds only themself by username; admin finds anyone.
    var owner = this.Caller.IsAdmin ? Owner.Anyone() : Owner.User(this.Caller.RequireSub());
    return new UserRes((await service.GetByUsername(owner, username, ct)).ToRes());
  }

  [Policy(Policies.Registered), HttpPost]
  public async Task<UserPrincipalRes> Create([FromBody] CreateUserReq req, CancellationToken ct)
  {
    var sub = this.Caller.RequireSub();
    var record = await tokens.Extract(sub, req.IdToken, req.AccessToken, ct);
    return (await service.Create(sub, record, ct)).ToRes();
  }

  [Policy(Policies.Registered), Subject("id"), HttpPut("{id}")]
  public async Task<UserPrincipalRes> Update(string id, [FromBody] UpdateUserReq req, CancellationToken ct)
  {
    var record = await tokens.Extract(id, req.IdToken, req.AccessToken, ct);
    return (await service.Update(Owner.User(id), id, record, ct)).ToRes();
  }

  /// <summary>Self-service deletion: the user id is the token's sub, never a route value.</summary>
  [Policy(Policies.Registered), HttpDelete("Me")]
  public async Task<IActionResult> DeleteMe(CancellationToken ct)
  {
    await service.DeleteAccount(this.Caller.RequireSub(), ct);
    return this.NoContent();
  }

  [Policy(Policies.Admin), HttpDelete("{id}")]
  public async Task<IActionResult> Delete(string id, CancellationToken ct)
  {
    await service.DeleteAllRemnants(id, ct);
    return this.NoContent();
  }
}
