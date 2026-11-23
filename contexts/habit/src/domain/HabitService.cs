using Ctx.Charity.Contract.V1;
using Ctx.Habit.Contract.V1;
using Ctx.Plan.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using LazyTax.Platform.Problems;

namespace Ctx.Habit.Domain;

/// <summary>
/// Habits CRUD (LazyTax <c>HabitService</c>, without complete/skip: check owns those). "Count, then write" runs
/// under the user lock in one unit of work; changes tell check through the <c>habit.changed</c> outbox event.
/// </summary>
public sealed class HabitService(
  IHabitRepository repo,
  IRoutineRepository routines,
  IPlanV1 plan,
  ICharityV1 charity,
  IOutbox outbox,
  IUnitOfWork uow,
  HabitSettings settings)
{
  public async Task<IReadOnlyList<HabitVersionView>> Search(Owner owner, HabitSearch search, CancellationToken ct) =>
    await repo.Search(owner, search, ct);

  public async Task<HabitVersionView> GetCurrent(Owner owner, Guid habitId, CancellationToken ct) =>
    await repo.GetCurrent(owner, habitId, ct) ?? throw new NotFound("Habit", habitId.ToString());

  public Task<HabitVersionView> Create(string userId, HabitInput input, Guid? routineId, CancellationToken ct)
  {
    var record = HabitRules.Validate(input, settings, "Invalid CreateHabitReq");
    return uow.Start(async () =>
    {
      // Count and insert under the user's lock, so concurrent creates can't both pass the count.
      await uow.LockUser(userId, ct);
      var quota = await plan.Quota(userId, QuotaKind.Habits, ct);
      if (await repo.Count(userId, ct) >= quota.Limit) throw new TierInsufficient(quota.Tier, quota.Key, quota.Limit);

      await this.EnsureCharity(record.CharityId, ct);

      Guid routine;
      if (routineId is { } rid)
      {
        // A supplied routine must be the user's own, live routine.
        var owned = await routines.Get(Owner.User(userId), rid, ct) ?? throw new NotFound("Routine", rid.ToString());
        routine = owned.Id;
      }
      else
      {
        // No routine supplied: the habit gets its own one-habit routine.
        routine = (await routines.Create(userId, record.Task, ct)).Id;
      }

      var created = await repo.Create(userId, record, routine, ct);
      await outbox.Add(HabitEvents.Changed, new HabitChanged(userId, [created.HabitId]), ct);
      return created;
    }, ct);
  }

  public Task<HabitVersionView> Update(string userId, Guid habitId, HabitInput input, bool enabled, CancellationToken ct)
  {
    var record = HabitRules.Validate(input, settings, "Invalid UpdateHabitReq");
    var owner = Owner.User(userId);
    return uow.Start(async () =>
    {
      // First read is owner-scoped: another user's habit is a 404.
      var habit = await repo.GetCurrent(owner, habitId, ct) ?? throw new NotFound("Habit", habitId.ToString());

      if (enabled && !habit.Enabled)
      {
        // Re-enabling does not change the habit count (disabled habits count), so it only checks.
        await uow.LockUser(userId, ct);
        var quota = await plan.Quota(userId, QuotaKind.Habits, ct);
        if (await repo.Count(userId, ct) >= quota.Limit) throw new TierInsufficient(quota.Tier, quota.Key, quota.Limit);
      }

      await this.EnsureCharity(record.CharityId, ct);
      var updated = await repo.Update(owner, habitId, record, enabled, ct) ?? throw new NotFound("Habit", habitId.ToString());

      // Days, deadline or enabled may have changed: check moves this habit's pending checks.
      await outbox.Add(HabitEvents.Changed, new HabitChanged(userId, [habitId]), ct);
      return updated;
    }, ct);
  }

  public Task Delete(string userId, Guid habitId, CancellationToken ct) =>
    uow.Start(async () =>
    {
      if (!await repo.Delete(Owner.User(userId), habitId, ct)) throw new NotFound("Habit", habitId.ToString());
      await outbox.Add(HabitEvents.Deleted, new HabitDeleted(userId, [habitId]), ct);
      await outbox.Add(HabitEvents.Changed, new HabitChanged(userId, [habitId]), ct);
    }, ct);

  /// <summary>Moves the listed habits to the top in the given order; returns the user's final order.</summary>
  public Task<IReadOnlyList<Guid>> Reorder(string userId, IReadOnlyList<Guid> habitIds, CancellationToken ct) =>
    uow.Start<IReadOnlyList<Guid>>(async () =>
    {
      var current = await repo.OrderedIds(Owner.User(userId), ct);
      var order = HabitOrder.Merge(current, habitIds);
      await repo.SetOrder(userId, order, ct);
      return order;
    }, ct);

  private async Task EnsureCharity(Guid? charityId, CancellationToken ct)
  {
    // The charity is another context's row: no foreign key, so ask its contract.
    if (charityId is { } id && !await charity.Exists(id, ct))
      throw new ValidationFailed("Invalid habit", "CharityId", $"Charity {id} does not exist");
  }
}
