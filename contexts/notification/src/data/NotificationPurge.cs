using LazyTax.Platform.Lifecycle;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Notification.Data;

/// <summary>Account deletion: the user's inbox, and every notification they caused (they are its actor).</summary>
public sealed class NotificationPurge(NotificationDb db) : IUserPurge
{
  public Task Purge(string userId, CancellationToken ct) =>
    db.InboxNotifications.Where(x => x.UserId == userId || x.ActorUserId == userId).ExecuteDeleteAsync(ct);
}
