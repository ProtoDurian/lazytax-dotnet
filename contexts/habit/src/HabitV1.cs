using Ctx.Account.Contract.V1;
using Ctx.Habit.Contract.V1;
using Ctx.Habit.Domain;
using LazyTax.Platform.Data;

namespace Ctx.Habit;

/// <summary>habit.v1, as other contexts (check, proof, friend) call it.</summary>
public sealed class HabitV1(IHabitRepository repo, IAccountV1 account) : IHabitV1
{
  public async Task<IReadOnlyList<SchedulableHabit>> ListSchedulable(
    IReadOnlyCollection<string>? userIds = null, IReadOnlyCollection<Guid>? habitIds = null, CancellationToken ct = default)
  {
    var rows = await repo.ListSchedulable(userIds, habitIds, ct);
    // The user's account timezone wins; fall back to the habit's when it is missing or unknown.
    var zones = new Dictionary<string, string?>();
    foreach (var u in rows.Select(r => r.UserId).Distinct())
      zones[u] = await account.GetTimezone(u, ct);
    return rows.Select(r => new SchedulableHabit(
      r.HabitId, r.UserId, r.VersionId, r.DaysOfWeek, r.DeadlineTime,
      zones[r.UserId] is { } tz && TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _) ? tz : r.HabitTimezone)).ToList();
  }

  public Task<HabitVersionView?> GetVersion(Owner owner, Guid versionId, CancellationToken ct = default) => repo.GetVersion(owner, versionId, ct);

  public Task<HabitVersionView?> GetCurrent(Owner owner, Guid habitId, CancellationToken ct = default) => repo.GetCurrent(owner, habitId, ct);

  public Task<IReadOnlyList<HabitVersionView>> GetCurrentMany(Owner owner, IReadOnlyCollection<Guid> habitIds, CancellationToken ct = default) =>
    repo.GetCurrentMany(owner, habitIds, ct);

  public Task<IReadOnlyList<HabitVersionView>> ListCurrent(Owner owner, int limit, int skip, CancellationToken ct = default) =>
    repo.Search(owner, new HabitSearch(null, null, null, limit, skip), ct);

  public Task<IReadOnlyList<HabitVersionView>> ListVersions(Owner owner, Guid habitId, CancellationToken ct = default) => repo.ListVersions(owner, habitId, ct);

  public async Task<bool> Owns(string userId, Guid habitId, CancellationToken ct = default) =>
    await repo.GetCurrent(Owner.User(userId), habitId, ct) is not null;

  public Task<int> CountHabits(string userId, CancellationToken ct = default) => repo.Count(userId, ct);
}
