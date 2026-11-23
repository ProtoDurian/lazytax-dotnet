using Ctx.Notification.Contract.V1;
using Ctx.Payment.Contract.V1;
using Ctx.Payment.Domain;

namespace Ctx.Payment.Data;

/// <summary>
/// The consent-changed security email through notification.v1 (LazyTax NotifyConsentChanged). notification
/// turns it into an outbox event, so this runs inside the caller's unit of work and sends after commit.
/// </summary>
public sealed class NotificationConsentNotifier(INotificationV1 notification) : IConsentNotifier
{
  public Task ConsentChanged(string userId, bool linked, ConsentPurpose purpose, DateTime atUtc, CancellationToken ct) =>
    notification.Email(userId, EmailTemplateIds.PaymentConsentChanged, new Dictionary<string, string>
    {
      ["ChangeSummary"] = linked ? "Payment method linked" : "Payment method unlinked",
      ["Purpose"] = purpose.Label(),
      ["ChangeDate"] = atUtc.ToString("dd MMM yyyy, HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture),
    }, ct);
}
