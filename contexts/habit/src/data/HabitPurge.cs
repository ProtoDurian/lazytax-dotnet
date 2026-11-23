using LazyTax.Platform.Lifecycle;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Habit.Data;

/// <summary>Account deletion: removes every habit, version (cascade) and routine of the user.</summary>
public sealed class HabitPurge(HabitDb db) : IUserPurge
{
  public async Task Purge(string userId, CancellationToken ct)
  {
    await db.Habits.Where(h => h.UserId == userId).ExecuteDeleteAsync(ct);
    await db.Routines.Where(r => r.UserId == userId).ExecuteDeleteAsync(ct);
  }
}
