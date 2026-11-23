using LazyTax.Platform.Problems;

namespace Ctx.Charity.Domain;

public sealed class CauseService(ICauseRepository repo)
{
  public async Task<CauseModel> Get(Guid id, CancellationToken ct) =>
    await repo.Get(id, ct) ?? throw new NotFound("Cause", id.ToString());

  public Task<CauseModel?> GetByKey(string key, CancellationToken ct) => repo.GetByKey(key, ct);

  public Task<IReadOnlyList<CauseModel>> Search(CauseSearch s, CancellationToken ct)
  {
    Validation.CauseSearch(s);
    return repo.Search(s, ct);
  }

  public Task<CauseModel> Create(CauseRecord r, CancellationToken ct)
  {
    Validation.Cause(r, keyRequired: true);
    return repo.Create(r, ct);
  }

  public async Task<CauseModel> Update(Guid id, CauseRecord r, CancellationToken ct)
  {
    Validation.Cause(r, keyRequired: false);
    return await repo.Update(id, r, ct) ?? throw new NotFound("Cause", id.ToString());
  }

  public async Task Delete(Guid id, CancellationToken ct)
  {
    if (!await repo.Delete(id, ct)) throw new NotFound("Cause", id.ToString());
  }
}
