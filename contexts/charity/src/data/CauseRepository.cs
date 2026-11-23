using Ctx.Charity.Domain;
using LazyTax.Platform.Problems;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Charity.Data;

/// <summary>The cause catalogue is global (no user column), so reads take no <c>Owner</c>.</summary>
public sealed class CauseRepository(CharityDb db) : ICauseRepository
{
  private static CauseModel ToModel(CauseRow r) => new(r.Id, new CauseRecord(r.Key, r.Name));

  public async Task<CauseModel?> Get(Guid id, CancellationToken ct) =>
    (await db.Causes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)) is { } r ? ToModel(r) : null;

  public async Task<IReadOnlyList<CauseModel>> Search(CauseSearch s, CancellationToken ct)
  {
    var q = db.Causes.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(s.Key)) q = q.Where(x => EF.Functions.ILike(x.Key, $"%{s.Key}%"));
    if (!string.IsNullOrWhiteSpace(s.Name)) q = q.Where(x => EF.Functions.ILike(x.Name, $"%{s.Name}%"));
    var rows = await q.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip(s.Skip).Take(s.Limit).ToListAsync(ct);
    return [.. rows.Select(ToModel)];
  }

  public async Task<CauseModel> Create(CauseRecord record, CancellationToken ct)
  {
    if (await db.Causes.AnyAsync(x => x.Key == record.Key, ct)) throw new Conflict("Cause already exists");
    var row = new CauseRow { Key = record.Key, Name = record.Name };
    db.Causes.Add(row);
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException) { throw new Conflict("Cause already exists"); }
    return ToModel(row);
  }

  public async Task<CauseModel?> Update(Guid id, CauseRecord record, CancellationToken ct)
  {
    var row = await db.Causes.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (row is null) return null;
    row.Name = record.Name; // the key is immutable
    await db.SaveChangesAsync(ct);
    return ToModel(row);
  }

  public async Task<bool> Delete(Guid id, CancellationToken ct) =>
    await db.Causes.Where(x => x.Id == id).ExecuteDeleteAsync(ct) > 0;

  public async Task<CauseModel?> GetByKey(string key, CancellationToken ct) =>
    (await db.Causes.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, ct)) is { } r ? ToModel(r) : null;
}
