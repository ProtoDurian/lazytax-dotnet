using Ctx.Plan.Domain;
using LazyTax.Platform.Lifecycle;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Plan.Data;

public sealed class SubscriptionRepository(PlanDb db) : ISubscriptionRepository
{
  public async Task<SubscriptionPrincipal?> GetByUser(string userId, CancellationToken ct)
  {
    var row = await db.UserSubscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
    return row is null ? null : ToPrincipal(row);
  }

  public async Task<SubscriptionPrincipal> Upsert(SubscriptionRecord r, CancellationToken ct)
  {
    var row = await db.UserSubscriptions.FirstOrDefaultAsync(x => x.UserId == r.UserId, ct);
    if (row is null)
    {
      row = new SubscriptionRow { Id = Guid.NewGuid(), UserId = r.UserId };
      db.UserSubscriptions.Add(row);
    }
    row.Tier = r.Tier;
    row.Status = (int)r.Status;
    row.PeriodStart = DateTime.SpecifyKind(r.PeriodStart, DateTimeKind.Utc);
    row.PeriodEnd = DateTime.SpecifyKind(r.PeriodEnd, DateTimeKind.Utc);
    row.CancelAtPeriodEnd = r.CancelAtPeriodEnd;
    row.NextTier = r.NextTier;
    row.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync(ct);
    return ToPrincipal(row);
  }

  private static SubscriptionPrincipal ToPrincipal(SubscriptionRow x) => new(
    x.Id,
    new SubscriptionRecord(x.UserId, x.Tier, (SubscriptionStatus)x.Status, x.PeriodStart, x.PeriodEnd, x.CancelAtPeriodEnd, x.NextTier),
    x.CreatedAt,
    x.UpdatedAt);
}

/// <summary>Deletes the user's subscription row.</summary>
public sealed class PlanPurge(PlanDb db) : IUserPurge
{
  public Task Purge(string userId, CancellationToken ct) =>
    db.UserSubscriptions.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
}
