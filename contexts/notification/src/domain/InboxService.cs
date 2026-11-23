using Ctx.Notification.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;

namespace Ctx.Notification.Domain;

public sealed class InboxService(IInboxRepository repo, IUnitOfWork uow)
{
  // Mirror the column lengths of the inbox table.
  public const int KindMaxLength = 32;
  public const int TitleMaxLength = 256;
  public const int BodyMaxLength = 1024;
  public const int DataMaxLength = 2048;
  public const int DedupeKeyMaxLength = 128;
  public const int MaxLimit = 50;

  public Task<InboxNotification> Post(string userId, InboxRecord record, CancellationToken ct)
  {
    var errors = new Dictionary<string, string[]>();
    CheckText(errors, "kind", record.Kind, KindMaxLength);
    CheckText(errors, "title", record.Title, TitleMaxLength);
    CheckText(errors, "body", record.Body, BodyMaxLength);
    if (record.Data is { Length: > DataMaxLength }) errors["data"] = [$"data must be at most {DataMaxLength} characters"];
    if (record.DedupeKey is { Length: > DedupeKeyMaxLength }) errors["dedupeKey"] = [$"dedupeKey must be at most {DedupeKeyMaxLength} characters"];
    if (errors.Count > 0) throw new ValidationFailed("Invalid inbox notification", errors);
    return uow.Start(() => repo.Create(userId, record, ct), ct);
  }

  public Task<IReadOnlyList<InboxNotification>> Search(Owner owner, bool? unreadOnly, int? limit, int? skip, CancellationToken ct)
  {
    var search = new InboxSearch(owner, unreadOnly ?? false, limit ?? 20, skip ?? 0);
    var errors = new Dictionary<string, string[]>();
    if (search.Limit is < 1 or > MaxLimit) errors["limit"] = [$"Limit must be between 1 and {MaxLimit}"];
    if (search.Skip < 0) errors["skip"] = ["Skip must not be negative"];
    if (errors.Count > 0) throw new ValidationFailed("Invalid search", errors);
    return repo.Search(search, ct);
  }

  public Task<int> UnreadCount(Owner owner, CancellationToken ct) => repo.UnreadCount(owner, ct);

  /// <summary>404 when the notification is not the owner's.</summary>
  public async Task MarkRead(Owner owner, Guid id, CancellationToken ct)
  {
    if (!await repo.MarkRead(owner, id, DateTime.UtcNow, ct)) throw new NotFound("Inbox Notification", id.ToString());
  }

  public Task<int> MarkAllRead(Owner owner, CancellationToken ct) => repo.MarkAllRead(owner, DateTime.UtcNow, ct);

  private static void CheckText(Dictionary<string, string[]> errors, string field, string? value, int max)
  {
    if (string.IsNullOrWhiteSpace(value)) errors[field] = [$"{field} must not be empty"];
    else if (value.Length > max) errors[field] = [$"{field} must be at most {max} characters"];
  }
}
