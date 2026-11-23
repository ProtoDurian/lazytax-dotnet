using Ctx.Penalty.Contract.V1;
using Ctx.Penalty.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ctx.Penalty.Api.V1;

/// <summary>
/// /api/v1/Penalty. LazyTax had no penalty controller (the drain was a hosted service only); these routes let a
/// user see what they owe and an admin see the ledger and run the drain.
/// </summary>
[Route("")]
public sealed class PenaltyController(PenaltyService service) : ContextController
{
  [Policy(Policies.Admin)]
  [HttpGet("admin/search")]
  public Task<IReadOnlyList<PenaltyInfo>> AdminSearch([FromQuery] string? userId, [FromQuery] string? status,
    [FromQuery] int? limit, [FromQuery] int? skip, CancellationToken ct) =>
    service.Search(string.IsNullOrWhiteSpace(userId) ? Owner.Anyone() : Owner.User(userId), Query(status, limit, skip), ct);

  [Policy(Policies.Admin)]
  [HttpGet("admin/charity-balances")]
  public Task<IReadOnlyList<CharityBalance>> CharityBalances(CancellationToken ct) => service.CharityBalances(ct);

  [Policy(Policies.Admin)]
  [HttpPost("admin/process")]
  public async Task<ProcessRes> Process(CancellationToken ct) => new(await service.ProcessPending(ct));

  [Policy(Policies.Registered), Subject("userId")]
  [HttpGet("{userId}")]
  public Task<IReadOnlyList<PenaltyInfo>> Search(string userId, [FromQuery] string? status, [FromQuery] int? limit,
    [FromQuery] int? skip, CancellationToken ct) =>
    service.Search(Owner.User(userId), Query(status, limit, skip), ct);

  [Policy(Policies.Registered), Subject("userId")]
  [HttpGet("{userId}/outstanding")]
  public Task<OutstandingDebt> Outstanding(string userId, CancellationToken ct) => service.Outstanding(userId, ct);

  [Policy(Policies.Registered), Subject("userId")]
  [HttpGet("{userId}/{id:guid}")]
  public Task<PenaltyInfo> Get(string userId, Guid id, CancellationToken ct) => service.GetOwned(Owner.User(userId), id, ct);

  internal static PenaltySearch Query(string? status, int? limit, int? skip) =>
    new(string.IsNullOrWhiteSpace(status) ? null : status.ToLowerInvariant(), limit ?? 20, skip ?? 0);
}

public sealed record ProcessRes(int Processed);
