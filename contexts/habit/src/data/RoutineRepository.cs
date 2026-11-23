using Ctx.Habit.Domain;
using LazyTax.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Habit.Data;

public sealed class RoutineRepository(HabitDb db) : IRoutineRepository
{
  private static RoutineView ToView(RoutineRow r) => new(r.Id, r.UserId, r.Name);

  public async Task<RoutineView?> Get(Owner owner, Guid id, CancellationToken ct)
  {
    var row = await db.Routines.AsNoTracking().OwnedBy(owner, r => r.UserId)
      .FirstOrDefaultAsync(r => r.Id == id && r.DeletedAt == null, ct);
    return row is null ? null : ToView(row);
  }

  public async Task<IReadOnlyList<RoutineView>> Search(Owner owner, RoutineSearch s, CancellationToken ct)
  {
    var q = db.Routines.AsNoTracking().OwnedBy(owner, r => r.UserId).Where(r => r.DeletedAt == null);
    if (!string.IsNullOrWhiteSpace(s.Name)) q = q.Where(r => EF.Functions.ILike(r.Name, $"%{s.Name}%"));
    var rows = await q.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Skip(s.Skip).Take(s.Limit).ToListAsync(ct);
    return rows.Select(ToView).ToList();
  }

  public async Task<RoutineView> Create(string userId, string name, CancellationToken ct)
  {
    var row = new RoutineRow { UserId = userId, Name = name };
    db.Routines.Add(row);
    await db.SaveChangesAsync(ct);
    db.ChangeTracker.Clear();
    return ToView(row);
  }

  public async Task<RoutineView?> Update(Owner owner, Guid id, string name, CancellationToken ct)
  {
    var affected = await db.Routines.OwnedBy(owner, r => r.UserId)
      .Where(r => r.Id == id && r.DeletedAt == null)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, name), ct);
    return affected == 0 ? null : await this.Get(owner, id, ct);
  }

  public async Task<IReadOnlyList<Guid>?> Delete(Owner owner, Guid id, CancellationToken ct)
  {
    var now = DateTime.UtcNow;
    var affected = await db.Routines.OwnedBy(owner, r => r.UserId)
      .Where(r => r.Id == id && r.DeletedAt == null)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.DeletedAt, now), ct);
    if (affected == 0) return null;

    var habitIds = await db.Habits.Where(h => h.RoutineId == id && h.DeletedAt == null).Select(h => h.Id).ToListAsync(ct);
    if (habitIds.Count > 0)
      await db.Habits.Where(h => habitIds.Contains(h.Id) && h.DeletedAt == null)
        .ExecuteUpdateAsync(s => s.SetProperty(h => h.DeletedAt, now), ct);
    return habitIds;
  }
}
