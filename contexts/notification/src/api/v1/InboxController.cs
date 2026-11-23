using Ctx.Notification.Contract.V1;
using Ctx.Notification.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Http;
using Microsoft.AspNetCore.Mvc;

namespace Ctx.Notification.Api.V1;

/// <summary>The user's in-app inbox. Other contexts post to it through <c>INotificationV1.Post</c>.</summary>
[Route("")]
[Policy(Policies.Registered)]
public sealed class InboxController(InboxService service) : ContextController
{
  /// <summary>Newest first. Limit 1..50 and Skip >= 0 are checked by the service (400 otherwise).</summary>
  [Subject("userId")]
  [HttpGet("{userId}")]
  public async Task<List<InboxNotificationRes>> Search(string userId, [FromQuery] SearchInboxQuery query, CancellationToken ct) =>
    (await service.Search(Owner.User(userId), query.UnreadOnly, query.Limit, query.Skip, ct)).Select(InboxNotificationRes.From).ToList();

  [Subject("userId")]
  [HttpGet("{userId}/unread-count")]
  public async Task<InboxCountRes> UnreadCount(string userId, CancellationToken ct) =>
    new(await service.UnreadCount(Owner.User(userId), ct));

  /// <summary>204 (also when already read), 404 when it is not this user's notification.</summary>
  [Subject("userId")]
  [HttpPost("{userId}/{id:guid}/read")]
  public async Task<IActionResult> MarkRead(string userId, Guid id, CancellationToken ct)
  {
    await service.MarkRead(Owner.User(userId), id, ct);
    return this.NoContent();
  }

  /// <summary>Count is how many were unread and are now read.</summary>
  [Subject("userId")]
  [HttpPost("{userId}/read-all")]
  public async Task<InboxCountRes> MarkAllRead(string userId, CancellationToken ct) =>
    new(await service.MarkAllRead(Owner.User(userId), ct));
}
