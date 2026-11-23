using Ctx.Notification.Contract.V1;
using Ctx.Notification.Data;
using Ctx.Notification.Domain;
using Ctx.Notification.Email;
using Ctx.Notification.Events;
using Ctx.Notification.Mcp;
using LazyTax.Platform.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ctx.Notification;

/// <summary>The one registration of the notification context.</summary>
public sealed class NotificationContext : IContext
{
  public void Register(ContextBuilder c)
  {
    c.Services.AddScoped<IInboxRepository, InboxRepository>();
    c.Services.AddScoped<InboxService>();
    c.Services.AddScoped<EmailRequests>();
    c.Services.AddScoped<EmailSender>();
    c.Services.AddScoped<PenaltyNotifier>();
    // The fake SMTP adapter and the email settings read their config lazily (tests change it after registration).
    c.Services.AddSingleton<IMailTransport>(sp => new FileMailTransport(
      sp.GetRequiredService<IConfiguration>()["Contexts:notification:MailDir"]
      ?? Path.Combine(Path.GetTempPath(), "lazytax-mail")));
    c.Services.AddSingleton(sp =>
    {
      var s = sp.GetRequiredService<IConfiguration>().GetSection("Contexts:notification");
      return new EmailSettings(s["BaseUrl"] ?? "https://lazytax.club", s["SupportEmail"] ?? "support@lazytax.club");
    });

    c.Provide<INotificationV1, NotificationV1>("notification.v1");
    c.Db<NotificationDb>();
    c.AddControllers();
    c.Tool(NotificationTools.List);
    c.Tool(NotificationTools.UnreadCount);
    c.Tool(NotificationTools.MarkRead);
    c.Tool(NotificationTools.MarkAllRead);
    c.Subscribe<EmailRequested, EmailRequestedHandler>(NotificationEvents.EmailRequested);
    c.Subscribe<PenaltyChargedEvent, PenaltyChargedHandler>("penalty.charged");
    c.Subscribe<PenaltyFailedEvent, PenaltyFailedHandler>("penalty.failed");
    c.PurgeUser<NotificationPurge>();
  }
}
