using Ctx.Protection.Domain;
using LazyTax.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Protection.Data;

public sealed class ProtectionRepository(ProtectionDb db) : IProtectionRepository
{
  public async Task<UserProtection?> Get(Owner owner, CancellationToken ct)
  {
    var row = await db.UserProtections.AsNoTracking().OwnedBy(owner, x => x.UserId).FirstOrDefaultAsync(ct);
    return row is null ? null : ToModel(row);
  }

  public async Task<UserProtection> Upsert(string userId, CancellationToken ct)
  {
    var row = await db.UserProtections.FirstOrDefaultAsync(x => x.UserId == userId, ct);
    if (row is null)
    {
      row = new UserProtectionRow { UserId = userId };
      db.UserProtections.Add(row);
      await db.SaveChangesAsync(ct);
    }
    return ToModel(row);
  }

  public async Task SetMaxStreak(string userId, int maxStreak, CancellationToken ct) =>
    await db.UserProtections.Where(x => x.UserId == userId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.MaxStreak, Math.Max(0, maxStreak)), ct);

  public async Task Clamp(string userId, int cap, CancellationToken ct)
  {
    var max = Math.Max(0, cap);
    await db.UserProtections.Where(x => x.UserId == userId && x.FreezeCurrent > max)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.FreezeCurrent, max).SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
  }

  public async Task Add(string userId, int n, CancellationToken ct)
  {
    var add = Math.Max(0, n);
    await db.UserProtections.Where(x => x.UserId == userId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.FreezeCurrent, x => x.FreezeCurrent + add).SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
  }

  public async Task<bool> TryConsume(string userId, DateOnly date, CancellationToken ct)
  {
    if (await db.FreezeConsumptions.AsNoTracking().AnyAsync(x => x.UserId == userId && x.Date == date, ct)) return true;
    var row = await db.UserProtections.FirstOrDefaultAsync(x => x.UserId == userId, ct);
    if (row is null)
    {
      row = new UserProtectionRow { UserId = userId };
      db.UserProtections.Add(row);
    }
    if (row.FreezeCurrent <= 0)
    {
      await db.SaveChangesAsync(ct);
      return false;
    }
    row.FreezeCurrent -= 1;
    row.UpdatedAt = DateTime.UtcNow;
    db.FreezeConsumptions.Add(new FreezeConsumptionRow { UserId = userId, Date = date });
    await db.SaveChangesAsync(ct);
    return true;
  }

  public async Task<bool> RecordAwardIfAbsent(string userId, Guid habitId, DateOnly weekStart, CancellationToken ct)
  {
    if (await db.FreezeAwards.AsNoTracking().AnyAsync(x => x.HabitId == habitId && x.WeekStart == weekStart, ct)) return false;
    db.FreezeAwards.Add(new FreezeAwardRow { UserId = userId, HabitId = habitId, WeekStart = weekStart });
    await db.SaveChangesAsync(ct);
    return true;
  }

  public async Task RecordSettled(string userId, Guid habitId, DateOnly date, string status, CancellationToken ct)
  {
    var row = await db.SettledDays.FirstOrDefaultAsync(x => x.HabitId == habitId && x.Date == date, ct);
    if (row is null)
      db.SettledDays.Add(new SettledDayRow { UserId = userId, HabitId = habitId, Date = date, Status = status });
    else
    {
      row.Status = status;
      row.SettledAt = DateTime.UtcNow;
    }
    await db.SaveChangesAsync(ct);
  }

  public async Task<IReadOnlyList<SettledDay>> SettledBetween(Owner owner, DateOnly from, DateOnly to, CancellationToken ct) =>
    await db.SettledDays.AsNoTracking().OwnedBy(owner, x => x.UserId)
      .Where(x => x.Date >= from && x.Date <= to)
      .Select(x => new SettledDay(x.HabitId, x.Date, x.Status))
      .ToListAsync(ct);

  public async Task<IReadOnlyList<string>> UsersSettledSince(DateOnly from, CancellationToken ct) =>
    await db.SettledDays.AsNoTracking().Where(x => x.Date >= from).Select(x => x.UserId).Distinct().OrderBy(x => x).ToListAsync(ct);

  public Task PruneSettledBefore(DateOnly before, CancellationToken ct) =>
    db.SettledDays.Where(x => x.Date < before).ExecuteDeleteAsync(ct);

  private static UserProtection ToModel(UserProtectionRow r) => new(r.UserId, r.FreezeCurrent, r.MaxStreak);
}
