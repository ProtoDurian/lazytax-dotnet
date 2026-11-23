using Ctx.Notification.Contract.V1;
using Ctx.Notification.Domain;
using LazyTax.Platform.Data;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Notification.Data;

public sealed class InboxRepository(NotificationDb db) : IInboxRepository
{
  public async Task<InboxNotification> Create(string userId, InboxRecord record, CancellationToken ct)
  {
    // Postgres keeps microseconds: truncate so what Create returns equals what Search later reads.
    var now = DateTime.UtcNow;
    var row = new InboxNotificationRow
    {
      Id = Guid.NewGuid(),
      UserId = userId,
      Kind = record.Kind,
      Title = record.Title,
      Body = record.Body,
      ActorUserId = record.ActorUserId,
      Data = record.Data,
      DedupeKey = record.DedupeKey,
      CreatedAtUtc = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc),
    };

    // ON CONFLICT instead of catching the unique violation: a violation would abort the caller's transaction.
    await db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO notification."InboxNotifications"
        ("Id","UserId","Kind","Title","Body","ActorUserId","Data","DedupeKey","CreatedAtUtc","ReadAtUtc")
      VALUES ({row.Id},{row.UserId},{row.Kind},{row.Title},{row.Body},{row.ActorUserId},{row.Data},{row.DedupeKey},{row.CreatedAtUtc},NULL)
      ON CONFLICT ("UserId","DedupeKey") WHERE "DedupeKey" IS NOT NULL DO NOTHING
      """, ct);

    // Not the one just inserted? Then the key was already posted: hand back that one.
    var saved = record.DedupeKey is null
      ? row
      : await db.InboxNotifications.AsNoTracking().Where(x => x.UserId == userId && x.DedupeKey == record.DedupeKey).FirstAsync(ct);
    return ToContract(saved);
  }

  public async Task<IReadOnlyList<InboxNotification>> Search(InboxSearch search, CancellationToken ct)
  {
    var query = db.InboxNotifications.AsNoTracking().OwnedBy(search.Owner, x => x.UserId);
    if (search.UnreadOnly) query = query.Where(x => x.ReadAtUtc == null);
    // Id breaks ties so pages never overlap or skip rows created at the same instant.
    var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
      .Skip(search.Skip).Take(search.Limit).ToListAsync(ct);
    return rows.Select(ToContract).ToList();
  }

  public Task<int> UnreadCount(Owner owner, CancellationToken ct) =>
    db.InboxNotifications.AsNoTracking().OwnedBy(owner, x => x.UserId).CountAsync(x => x.ReadAtUtc == null, ct);

  public async Task<bool> MarkRead(Owner owner, Guid id, DateTime atUtc, CancellationToken ct)
  {
    var at = DateTime.SpecifyKind(atUtc, DateTimeKind.Utc);
    // An already-read notification still matches (so the call is idempotent) but keeps its first read time.
    var affected = await db.InboxNotifications.OwnedBy(owner, x => x.UserId).Where(x => x.Id == id)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAtUtc, x => x.ReadAtUtc ?? at), ct);
    return affected > 0;
  }

  public Task<int> MarkAllRead(Owner owner, DateTime atUtc, CancellationToken ct)
  {
    var at = DateTime.SpecifyKind(atUtc, DateTimeKind.Utc);
    return db.InboxNotifications.OwnedBy(owner, x => x.UserId).Where(x => x.ReadAtUtc == null)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAtUtc, at), ct);
  }

  private static InboxNotification ToContract(InboxNotificationRow r) =>
    new(r.Id, r.UserId, r.Kind, r.Title, r.Body, r.ActorUserId, r.Data, r.CreatedAtUtc, r.ReadAtUtc);
}
