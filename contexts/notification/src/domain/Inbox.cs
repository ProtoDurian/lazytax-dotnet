using LazyTax.Platform.Data;

namespace Ctx.Notification.Domain;

/// <summary>What a post carries (LazyTax <c>InboxNotificationRecord</c>).</summary>
public sealed record InboxRecord(
  string Kind,
  string Title,
  string Body,
  string? ActorUserId,
  string? Data,
  string? DedupeKey);

/// <summary>One page of the user's inbox: newest first.</summary>
public sealed record InboxSearch(Owner Owner, bool UnreadOnly, int Limit, int Skip);

/// <summary>The inbox as the domain sees it; the data layer implements it. Every read takes an <see cref="Owner"/>.</summary>
public interface IInboxRepository
{
  /// <summary>Inserts, or returns the existing row when this user already has the same dedupe key.</summary>
  Task<Ctx.Notification.Contract.V1.InboxNotification> Create(string userId, InboxRecord record, CancellationToken ct);

  Task<IReadOnlyList<Ctx.Notification.Contract.V1.InboxNotification>> Search(InboxSearch search, CancellationToken ct);

  Task<int> UnreadCount(Owner owner, CancellationToken ct);

  /// <summary>False when the notification is not the owner's (idempotent for one that is already read).</summary>
  Task<bool> MarkRead(Owner owner, Guid id, DateTime atUtc, CancellationToken ct);

  Task<int> MarkAllRead(Owner owner, DateTime atUtc, CancellationToken ct);
}
