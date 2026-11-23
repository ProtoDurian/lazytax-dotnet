using Ctx.Notification.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using LazyTax.Platform.Problems;

namespace Ctx.Notification.Domain;

/// <summary>A message ready for a mail server.</summary>
public sealed record MailMessage(string MessageId, string To, string Subject, string Html);

/// <summary>The SMTP adapter. LazyTax has a real one; here the only one is a fake that writes a file (no network).</summary>
public interface IMailTransport
{
  /// <summary>Idempotent by <see cref="MailMessage.MessageId"/>: sending one twice leaves one message.</summary>
  Task Send(MailMessage message, CancellationToken ct);
}

/// <summary>Where emails go when requested: an outbox event, so the send happens after the caller's commit.</summary>
public sealed class EmailRequests(IOutbox outbox, IUnitOfWork uow)
{
  public Task Request(string userId, string template, IReadOnlyDictionary<string, string>? data, CancellationToken ct)
  {
    if (!EmailTemplates.Exists(template))
      throw new ValidationFailed("Unknown email template", "template", $"'{template}' is not an email template");
    var payload = new EmailRequested(userId, template, new Dictionary<string, string>(data ?? new Dictionary<string, string>()));
    return uow.Start(() => outbox.Add(NotificationEvents.EmailRequested, payload, ct), ct);
  }
}
