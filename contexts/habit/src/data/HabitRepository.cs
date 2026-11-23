using Ctx.Habit.Contract.V1;
using Ctx.Habit.Domain;
using LazyTax.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Habit.Data;

/// <summary>EF storage for habits. Every read is owner-scoped through the habit's UserId (layer 4).</summary>
public sealed class HabitRepository(HabitDb db) : IHabitRepository
{
  private static HabitVersionView ToView(HabitRow h, HabitVersionRow v) => new(
    v.Id, h.Id, h.UserId, (ushort)v.Version, v.Task, v.DaysOfWeek, v.NotificationTime, v.DeadlineTime, v.CharityId,
    v.StakeCents, v.StakeCurrency, v.RatioBasisPoints, v.Timezone, h.Enabled, h.DeletedAt != null,
    h.Version == v.Version, h.SortOrder, h.RoutineId);

  private sealed class Pair
  {
    public required HabitRow H { get; init; }
    public required HabitVersionRow V { get; init; }
  }

  private IQueryable<Pair> Current(Owner owner) =>
    db.Habits.AsNoTracking().OwnedBy(owner, h => h.UserId)
      .Where(h => h.DeletedAt == null)
      .Join(db.HabitVersions, h => new { h.Id, h.Version }, v => new { Id = v.HabitId, v.Version }, (h, v) => new Pair { H = h, V = v });

  private static async Task<IReadOnlyList<HabitVersionView>> Views(IQueryable<Pair> q, CancellationToken ct)
  {
    var rows = await q.ToListAsync(ct);
    return rows.Select(x => ToView(x.H, x.V)).ToList();
  }

  public async Task<HabitVersionView?> GetCurrent(Owner owner, Guid habitId, CancellationToken ct) =>
    (await Views(this.Current(owner).Where(x => x.H.Id == habitId), ct)).FirstOrDefault();

  public async Task<HabitVersionView?> GetVersion(Owner owner, Guid versionId, CancellationToken ct)
  {
    // version -> habit -> user, in one query: another user's version id reads as null.
    var row = await db.HabitVersions.AsNoTracking()
      .OwnedBy(owner, v => v.Habit!.UserId)
      .Where(v => v.Id == versionId)
      .Select(v => new { H = v.Habit!, V = v })
      .FirstOrDefaultAsync(ct);
    return row is null ? null : ToView(row.H, row.V);
  }

  public Task<IReadOnlyList<HabitVersionView>> GetCurrentMany(Owner owner, IReadOnlyCollection<Guid> habitIds, CancellationToken ct)
  {
    var ids = habitIds.ToArray();
    return Views(this.Current(owner).Where(x => ids.Contains(x.H.Id)), ct);
  }

  public Task<IReadOnlyList<HabitVersionView>> Search(Owner owner, HabitSearch s, CancellationToken ct)
  {
    var q = this.Current(owner);
    if (s.Id is { } id) q = q.Where(x => x.H.Id == id);
    if (!string.IsNullOrWhiteSpace(s.Task)) q = q.Where(x => EF.Functions.ILike(x.V.Task, $"%{s.Task}%"));
    if (s.Enabled is { } en) q = q.Where(x => x.H.Enabled == en);
    // The user's habit order; Id breaks ties so pages never overlap.
    return Views(q.OrderBy(x => x.H.SortOrder).ThenBy(x => x.H.Id).Skip(s.Skip).Take(s.Limit), ct);
  }

  public async Task<IReadOnlyList<HabitVersionView>> ListVersions(Owner owner, Guid habitId, CancellationToken ct)
  {
    var rows = await db.HabitVersions.AsNoTracking()
      .OwnedBy(owner, v => v.Habit!.UserId)
      .Where(v => v.HabitId == habitId && v.Habit!.DeletedAt == null)
      .OrderByDescending(v => v.Version)
      .Select(v => new { H = v.Habit!, V = v })
      .ToListAsync(ct);
    return rows.Select(x => ToView(x.H, x.V)).ToList();
  }

  public async Task<IReadOnlyList<SchedulableRow>> ListSchedulable(IReadOnlyCollection<string>? userIds, IReadOnlyCollection<Guid>? habitIds, CancellationToken ct)
  {
    var q = db.Habits.AsNoTracking().Where(h => h.Enabled && h.DeletedAt == null);
    if (userIds is not null) { var u = userIds.ToArray(); q = q.Where(h => u.Contains(h.UserId)); }
    if (habitIds is not null) { var i = habitIds.ToArray(); q = q.Where(h => i.Contains(h.Id)); }
    var rows = await q.Join(db.HabitVersions, h => new { h.Id, h.Version }, v => new { Id = v.HabitId, v.Version },
        (h, v) => new { h.Id, h.UserId, VersionId = v.Id, v.DaysOfWeek, v.DeadlineTime, v.Timezone })
      .ToListAsync(ct);
    return rows.Select(x => new SchedulableRow(x.Id, x.UserId, x.VersionId, x.DaysOfWeek, x.DeadlineTime, x.Timezone)).ToList();
  }

  public Task<int> Count(string userId, CancellationToken ct) =>
    db.Habits.CountAsync(h => h.UserId == userId && h.DeletedAt == null, ct);

  public async Task<HabitVersionView> Create(string userId, VersionRecord r, Guid routineId, CancellationToken ct)
  {
    // A new habit goes to the bottom of the user's list.
    var last = await db.Habits.Where(h => h.UserId == userId && h.DeletedAt == null).MaxAsync(h => (int?)h.SortOrder, ct);
    var habit = new HabitRow { UserId = userId, Version = 1, Enabled = true, RoutineId = routineId, SortOrder = last + 1 ?? 0 };
    var version = NewVersion(habit.Id, 1, r);
    db.Habits.Add(habit);
    db.HabitVersions.Add(version);
    await db.SaveChangesAsync(ct);
    db.ChangeTracker.Clear();
    return ToView(habit, version);
  }

  public async Task<HabitVersionView?> Update(Owner owner, Guid habitId, VersionRecord r, bool enabled, CancellationToken ct)
  {
    var affected = await db.Habits.OwnedBy(owner, h => h.UserId)
      .Where(h => h.Id == habitId && h.DeletedAt == null)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.Version, x => x.Version + 1).SetProperty(x => x.Enabled, enabled), ct);
    if (affected == 0) return null;

    var habit = await db.Habits.AsNoTracking().FirstAsync(h => h.Id == habitId, ct);
    var version = NewVersion(habitId, habit.Version, r);
    db.HabitVersions.Add(version);
    await db.SaveChangesAsync(ct);
    db.ChangeTracker.Clear();
    return ToView(habit, version);
  }

  public async Task<bool> Delete(Owner owner, Guid habitId, CancellationToken ct)
  {
    var now = DateTime.UtcNow;
    var affected = await db.Habits.OwnedBy(owner, h => h.UserId)
      .Where(h => h.Id == habitId && h.DeletedAt == null)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.DeletedAt, now), ct);
    if (affected == 0) return false;

    // A routine lives as long as it has a live habit: soft delete it when this was its last one.
    var routineId = await db.Habits.AsNoTracking().Where(h => h.Id == habitId).Select(h => h.RoutineId).FirstAsync(ct);
    if (routineId is { } rid)
      await db.Routines.Where(r => r.Id == rid && r.DeletedAt == null && !db.Habits.Any(h => h.RoutineId == r.Id && h.DeletedAt == null))
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.DeletedAt, now), ct);
    return true;
  }

  public async Task<IReadOnlyList<Guid>> OrderedIds(Owner owner, CancellationToken ct) =>
    await db.Habits.AsNoTracking().OwnedBy(owner, h => h.UserId).Where(h => h.DeletedAt == null)
      .OrderBy(h => h.SortOrder).ThenBy(h => h.Id).Select(h => h.Id).ToListAsync(ct);

  public async Task SetOrder(string userId, IReadOnlyList<Guid> orderedIds, CancellationToken ct)
  {
    // One statement, so the whole order is written or none of it. Ids that are not the user's live habits match no row.
    var ids = orderedIds.ToArray();
    await db.Database.ExecuteSqlAsync($@"
      UPDATE habit.""Habits"" h SET ""SortOrder"" = o.idx - 1
      FROM unnest({ids}::uuid[]) WITH ORDINALITY AS o(id, idx)
      WHERE h.""Id"" = o.id AND h.""UserId"" = {userId} AND h.""DeletedAt"" IS NULL", ct);
  }

  private static HabitVersionRow NewVersion(Guid habitId, int version, VersionRecord r) => new()
  {
    HabitId = habitId,
    Version = version,
    CharityId = r.CharityId,
    Task = r.Task,
    DaysOfWeek = r.DaysOfWeek,
    NotificationTime = r.NotificationTime,
    DeadlineTime = r.DeadlineTime,
    StakeCents = r.StakeCents,
    StakeCurrency = r.StakeCurrency,
    RatioBasisPoints = r.RatioBasisPoints,
    Timezone = r.Timezone,
  };
}
