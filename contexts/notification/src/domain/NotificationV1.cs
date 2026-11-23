using Ctx.Notification.Contract.V1;
using LazyTax.Platform.Data;

namespace Ctx.Notification.Domain;

/// <summary>notification.v1 as other contexts call it.</summary>
public sealed class NotificationV1(InboxService inbox, EmailRequests emails) : INotificationV1
{
  public Task<InboxNotification> Post(string userId, string kind, string title, string body, string? actorUserId = null,
    string? data = null, string? dedupeKey = null, CancellationToken ct = default) =>
    inbox.Post(userId, new InboxRecord(kind, title, body, actorUserId, data, dedupeKey), ct);

  public Task Email(string userId, string template, IReadOnlyDictionary<string, string>? data = null, CancellationToken ct = default) =>
    emails.Request(userId, template, data, ct);

  public Task<int> UnreadCount(string userId, CancellationToken ct = default) => inbox.UnreadCount(Owner.User(userId), ct);
}
