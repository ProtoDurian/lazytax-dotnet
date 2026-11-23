using Ctx.Account.Contract.V1;
using Ctx.Plan.Contract.V1;
using Ctx.Protection.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using LazyTax.Platform.Problems;

namespace Ctx.Protection.Domain;

/// <summary>
/// The freeze pool (LazyTax <c>FreezeCapService</c>, <c>ProtectionRepository.TryConsumeFreeze</c> and
/// <c>ProtectionAwardService</c>). Every write runs in one unit of work, user lock first.
/// </summary>
public sealed class ProtectionService(
  IProtectionRepository repo,
  IPlanV1 plan,
  IAccountV1 account,
  IUnitOfWork uow,
  IOutbox outbox)
{
  /// <summary>Settled days older than this are pruned; the award only looks at last week.</summary>
  public const int KeepSettledDays = 45;

  public Task<FreezeBalance> ReconcileCap(string userId, int maxStreak, CancellationToken ct) =>
    uow.Start(async () =>
    {
      await uow.LockUser(userId, ct);
      await repo.Upsert(userId, ct);
      await repo.SetMaxStreak(userId, maxStreak, ct);
      return await CutToCap(userId, maxStreak, ct);
    }, ct);

  /// <summary>Reading the balance cuts it to the cap first, so Balance is never above Cap.</summary>
  public Task<FreezeBalance> Balance(Owner owner, CancellationToken ct)
  {
    if (owner.IsAnyone) throw new ArgumentException("balance needs a user owner", nameof(owner));
    var userId = owner.UserId!;
    return uow.Start(async () =>
    {
      await uow.LockUser(userId, ct);
      var row = await repo.Upsert(userId, ct);
      return await CutToCap(userId, row.MaxStreak, ct);
    }, ct);
  }

  public Task<bool> TryConsumeFreeze(string userId, DateOnly date, CancellationToken ct) =>
    uow.Start(async () =>
    {
      await uow.LockUser(userId, ct);
      return await repo.TryConsume(userId, date, ct);
    }, ct);

  /// <summary>Records a settled day from <c>check.settled</c> and remembers the max streak.</summary>
  public Task RecordSettled(string userId, Guid habitId, DateOnly date, string status, int? maxStreak, CancellationToken ct) =>
    uow.Start(async () =>
    {
      await uow.LockUser(userId, ct);
      await repo.Upsert(userId, ct);
      await repo.RecordSettled(userId, habitId, date, status, ct);
      if (maxStreak is { } m) await repo.SetMaxStreak(userId, m, ct);
    }, ct);

  /// <summary>
  /// Awards one freeze per habit that had a perfect last week, once ever per (habit, week). Returns the awards made.
  /// A user without a configuration has nothing to award (LazyTax returned 0).
  /// </summary>
  public async Task<int> AwardWeekly(string userId, DateTime? nowUtc, CancellationToken ct)
  {
    UserToday today;
    try { today = await account.Today(userId, nowUtc, ct); }
    catch (Problem p) when (p.Status == 404) { return 0; }

    var (start, end) = Weeks.Previous(today.Today);
    var days = await repo.SettledBetween(Owner.User(userId), start, end, ct);
    var perfect = days.GroupBy(d => d.HabitId)
      .Where(g => Weeks.IsPerfect([.. g.Select(d => d.Status)]))
      .Select(g => g.Key)
      .Order()
      .ToList();

    var awards = 0;
    foreach (var habitId in perfect)
    {
      var awarded = await uow.Start(async () =>
      {
        await uow.LockUser(userId, ct);
        if (!await repo.RecordAwardIfAbsent(userId, habitId, start, ct)) return false;
        var row = await repo.Upsert(userId, ct);
        await repo.Add(userId, 1, ct);
        var b = await CutToCap(userId, row.MaxStreak, ct);
        await outbox.Add(ProtectionEvents.Awarded, new ProtectionAwarded(userId, habitId, start, b.Balance, b.Cap), ct);
        return true;
      }, ct);
      if (awarded) awards++;
    }
    return awards;
  }

  /// <summary>The worker's sweep: users who settled days in the last 14 days, then prune old days.</summary>
  public async Task<int> AwardWeeklyForRecentUsers(DateTime nowUtc, CancellationToken ct, Action<string, Exception>? onError = null)
  {
    var today = DateOnly.FromDateTime(nowUtc);
    var users = await repo.UsersSettledSince(today.AddDays(-14), ct);
    var total = 0;
    foreach (var userId in users)
    {
      try { total += await AwardWeekly(userId, nowUtc, ct); }
      catch (Exception e) when (e is not OperationCanceledException) { onError?.Invoke(userId, e); }
    }
    await uow.Start(() => repo.PruneSettledBefore(today.AddDays(-KeepSettledDays), ct), ct);
    return total;
  }

  private async Task<FreezeBalance> CutToCap(string userId, int maxStreak, CancellationToken ct)
  {
    var cap = await plan.FreezeCap(userId, maxStreak, ct);
    await repo.Clamp(userId, cap, ct);
    var row = await repo.Get(Owner.User(userId), ct) ?? throw new NotFound("Protection", userId);
    return new FreezeBalance(userId, row.FreezeCurrent, cap);
  }
}
