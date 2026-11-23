using Ctx.Account.Contract.V1;
using Ctx.Notification.Contract.V1;
using LazyTax.Platform.Data;
using Microsoft.Extensions.Logging;

namespace Ctx.Notification.Domain;

/// <summary>Contexts:notification settings the emails need (defaults are LazyTax's).</summary>
public sealed record EmailSettings(string BaseUrl, string SupportEmail);

/// <summary>
/// Resolve the recipient, render, send: entirely best effort. It never throws (apart from cancellation): an
/// email can never fail, roll back or delay the event whose news it carries, and a retry of the outbox event
/// would only send it twice.
/// </summary>
public sealed class EmailSender(IAccountV1 account, IMailTransport transport, EmailSettings settings, ILogger<EmailSender> logger)
{
  public async Task Send(Guid eventId, EmailRequested request, CancellationToken ct)
  {
    try
    {
      var profile = await account.GetProfile(Owner.User(request.UserId), ct);
      if (profile is null || string.IsNullOrWhiteSpace(profile.Email))
      {
        logger.LogWarning("Email '{Template}' skipped for user {UserId}: no recipient address", request.Template, request.UserId);
        return;
      }

      var vars = new Dictionary<string, string>(request.Data)
      {
        ["UserName"] = string.IsNullOrWhiteSpace(profile.Username) ? "there" : profile.Username,
        ["BaseUrl"] = settings.BaseUrl,
        ["SupportEmail"] = settings.SupportEmail,
      };
      var rendered = EmailTemplates.Render(request.Template, vars);
      await transport.Send(new MailMessage(eventId.ToString("N"), profile.Email, rendered.Subject, rendered.Html), ct);
      logger.LogInformation("Email '{Template}' sent to user {UserId}", request.Template, request.UserId);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogError(ex, "Email '{Template}' to user {UserId} failed", request.Template, request.UserId);
    }
  }
}
