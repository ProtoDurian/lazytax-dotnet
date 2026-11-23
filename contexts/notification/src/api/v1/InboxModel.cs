using Ctx.Notification.Contract.V1;

namespace Ctx.Notification.Api.V1;

/// <summary>Defaults: all notifications, 20 per page, from the newest.</summary>
public sealed record SearchInboxQuery(bool? UnreadOnly, int? Limit, int? Skip);

/// <summary>Data is the JSON text the app uses for the tap action.</summary>
public sealed record InboxNotificationRes(
  Guid Id, string Kind, string Title, string Body, string? ActorUserId, string? Data, DateTime CreatedAtUtc, DateTime? ReadAtUtc)
{
  public static InboxNotificationRes From(InboxNotification n) =>
    new(n.Id, n.Kind, n.Title, n.Body, n.ActorUserId, n.Data, n.CreatedAtUtc, n.ReadAtUtc);
}

public sealed record InboxCountRes(int Count);
