using Ctx.Charity.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;

namespace Ctx.Charity.Domain;

public sealed class CharityService(ICharityRepository repo, IUnitOfWork uow)
{
  public async Task<CharityModel> Get(Guid id, CancellationToken ct) =>
    await repo.Get(id, ct) ?? throw new NotFound("Charity", id.ToString());

  public Task<IReadOnlyList<CharityModel>> Search(CharitySearchQuery q, CancellationToken ct)
  {
    Validation.SearchCountries(q.Country, q.PrimaryRegistrationCountry);
    Validation.Paging(q.Limit, q.Skip);
    return repo.Search(q, ct);
  }

  public Task<CharityModel> Create(CharityRecord record, CancellationToken ct) => repo.Create(Validation.Charity(record), ct);

  public async Task<CharityModel> Update(Guid id, CharityRecord record, CancellationToken ct) =>
    await repo.Update(id, Validation.Charity(record), ct) ?? throw new NotFound("Charity", id.ToString());

  public async Task Delete(Guid id, CancellationToken ct)
  {
    if (!await repo.Delete(id, ct)) throw new NotFound("Charity", id.ToString());
  }

  public Task SetCauses(Guid id, string[]? keys, CancellationToken ct)
  {
    Validation.CauseKeys(keys);
    return uow.Start(async () =>
    {
      if (!await repo.SetCauses(id, keys!, ct)) throw new NotFound("Charity", id.ToString());
    }, ct);
  }

  public Task<CharityModel?> Find(Guid id, CancellationToken ct) => repo.Get(id, ct);

  public Task<bool> Exists(Guid id, CancellationToken ct) => repo.Exists(id, ct);

  public Task<CharityModel?> GetByExternalId(string source, string key, CancellationToken ct) => repo.GetByExternalId(source, key, ct);

  public Task<string?> GetExternalKey(Guid charityId, string source, CancellationToken ct) => repo.GetExternalKey(charityId, source, ct);

  public Task<BulkUpsertResult> BulkUpsert(IReadOnlyList<BulkUpsertCharity> items, CancellationToken ct) =>
    uow.Start(() => repo.BulkUpsert(items, ct), ct);
}
