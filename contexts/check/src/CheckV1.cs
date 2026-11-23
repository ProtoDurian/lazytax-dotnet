using Ctx.Check.Contract.V1;
using Ctx.Check.Domain;
using LazyTax.Platform.Data;

namespace Ctx.Check;

/// <summary>check.v1 for other contexts (proof reports evidence; penalty, friend, protection read executions).</summary>
internal sealed class CheckV1(CheckService checks, ICheckStore store) : ICheckV1
{
  public Task<HabitExecution?> Report(string userId, Guid habitId, Evidence evidence, CancellationToken ct = default) =>
    checks.Report(userId, habitId, evidence, null, ct);

  public Task<DailyCheck?> PendingCheckAt(string userId, Guid habitId, DateTime atUtc, CancellationToken ct = default) =>
    checks.PendingCheckAt(userId, habitId, atUtc, null, ct);

  public async Task<IReadOnlyList<HabitStreak>> Streaks(string userId, IReadOnlyList<Guid> habitIds, CancellationToken ct = default)
  {
    var tz = CheckWindow.Resolve(null);
    var today = CheckWindow.LocalDate(DateTime.UtcNow, tz).AddDays(1); // every recorded day counts, in any zone
    var byHabit = await store.ExecutionsForHabits(Owner.User(userId), habitIds, ct);
    return
    [
      .. habitIds.Distinct().Select(id =>
      {
        // Owner-scoped: another user's habits read as zeros.
        var days = (byHabit.GetValueOrDefault(id) ?? []);
        return new HabitStreak(id, Domain.Streaks.Current(days, today), Domain.Streaks.Max(days));
      }),
    ];
  }

  public Task<int> MaxStreak(string userId, CancellationToken ct = default) => store.MaxStreak(userId, ct);

  public Task<HabitExecution?> GetExecution(Owner owner, Guid executionId, CancellationToken ct = default) =>
    store.GetExecution(owner, executionId, ct);

  public async Task<bool> OwnsExecution(string userId, Guid executionId, CancellationToken ct = default) =>
    await store.GetExecution(Owner.User(userId), executionId, ct) is not null;

  public Task<HabitExecution?> ExecutionForDate(Owner owner, Guid habitId, DateOnly date, CancellationToken ct = default) =>
    store.ExecutionForDate(owner, habitId, date, ct);
}
