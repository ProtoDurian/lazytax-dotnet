using Ctx.Vacation.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Lifecycle;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Vacation.Data;

public sealed class VacationRepository(VacationDb db) : IVacationRepository
{
  private IQueryable<VacationPeriodRow> Of(Owner owner) => db.VacationPeriods.AsNoTracking().OwnedBy(owner, x => x.UserId);

  public async Task<VacationPrincipal?> Get(Owner owner, Guid id, CancellationToken ct)
  {
    var row = await this.Of(owner).FirstOrDefaultAsync(x => x.Id == id, ct);
    return row is null ? null : ToPrincipal(row);
  }

  public async Task<IReadOnlyList<VacationPrincipal>> Search(Owner owner, VacationSearch s, CancellationToken ct)
  {
    var q = this.Of(owner);
    if (s.Year is { } y) q = q.Where(x => x.StartDate.Year == y || x.EndDate.Year == y);
    var rows = await q.OrderByDescending(x => x.StartDate).Skip(s.Skip).Take(s.Limit).ToListAsync(ct);
    return [.. rows.Select(ToPrincipal)];
  }

  public async Task<IReadOnlyList<VacationPrincipal>> ListActive(Owner owner, DateOnly date, CancellationToken ct)
  {
    var rows = await this.Of(owner).Where(x => x.StartDate <= date && x.EndDate >= date).ToListAsync(ct);
    return [.. rows.Select(ToPrincipal)];
  }

  public Task<int> CountWindowsForYear(Owner owner, int year, CancellationToken ct) =>
    this.Of(owner).CountAsync(x => x.StartDate.Year == year, ct);

  public Task<bool> HasOverlap(Owner owner, DateOnly start, DateOnly end, CancellationToken ct) =>
    this.Of(owner).AnyAsync(x => start <= x.EndDate && end >= x.StartDate, ct);

  public async Task<VacationPrincipal> Create(string userId, VacationRecord r, CancellationToken ct)
  {
    var row = new VacationPeriodRow { Id = Guid.NewGuid(), UserId = userId, StartDate = r.StartDate, EndDate = r.EndDate, Timezone = r.Timezone };
    db.VacationPeriods.Add(row);
    await db.SaveChangesAsync(ct);
    return ToPrincipal(row);
  }

  public async Task<VacationPrincipal?> Update(Owner owner, Guid id, VacationRecord r, CancellationToken ct)
  {
    var row = await db.VacationPeriods.OwnedBy(owner, x => x.UserId).FirstOrDefaultAsync(x => x.Id == id, ct);
    if (row is null) return null;
    row.StartDate = r.StartDate;
    row.EndDate = r.EndDate;
    row.Timezone = r.Timezone;
    await db.SaveChangesAsync(ct);
    return ToPrincipal(row);
  }

  public async Task<bool> Delete(Owner owner, Guid id, CancellationToken ct) =>
    await db.VacationPeriods.OwnedBy(owner, x => x.UserId).Where(x => x.Id == id).ExecuteDeleteAsync(ct) > 0;

  private static VacationPrincipal ToPrincipal(VacationPeriodRow x) =>
    new(x.Id, x.UserId, new VacationRecord(x.StartDate, x.EndDate, x.Timezone), x.CreatedAt);
}

/// <summary>Deletes the user's vacation periods.</summary>
public sealed class VacationPurge(VacationDb db) : IUserPurge
{
  public Task Purge(string userId, CancellationToken ct) =>
    db.VacationPeriods.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
}
