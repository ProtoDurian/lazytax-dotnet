using Ctx.Habit.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using LazyTax.Platform.Problems;

namespace Ctx.Habit.Domain;

/// <summary>Routines CRUD (LazyTax <c>RoutineService</c>). Deleting a routine soft deletes its habits.</summary>
public sealed class RoutineService(IRoutineRepository repo, IOutbox outbox, IUnitOfWork uow)
{
  public static string ValidName(string? name)
  {
    if (string.IsNullOrWhiteSpace(name)) throw new ValidationFailed("Invalid routine", "Name", "'Name' must not be empty.");
    if (name.Length > 256) throw new ValidationFailed("Invalid routine", "Name", "The length of 'Name' must be 256 characters or fewer.");
    return name;
  }

  public async Task<IReadOnlyList<RoutineView>> Search(Owner owner, RoutineSearch search, CancellationToken ct) =>
    await repo.Search(owner, search, ct);

  public async Task<RoutineView> Get(Owner owner, Guid id, CancellationToken ct) =>
    await repo.Get(owner, id, ct) ?? throw new NotFound("Routine", id.ToString());

  public async Task<RoutineView> Create(string userId, string? name, CancellationToken ct) =>
    await repo.Create(userId, ValidName(name), ct);

  public async Task<RoutineView> Update(string userId, Guid id, string? name, CancellationToken ct) =>
    await repo.Update(Owner.User(userId), id, ValidName(name), ct) ?? throw new NotFound("Routine", id.ToString());

  public Task Delete(string userId, Guid id, CancellationToken ct) =>
    uow.Start(async () =>
    {
      var habitIds = await repo.Delete(Owner.User(userId), id, ct) ?? throw new NotFound("Routine", id.ToString());
      if (habitIds.Count == 0) return;
      Guid[] ids = [.. habitIds];
      await outbox.Add(HabitEvents.Deleted, new HabitDeleted(userId, ids), ct);
      await outbox.Add(HabitEvents.Changed, new HabitChanged(userId, ids), ct);
    }, ct);
}
