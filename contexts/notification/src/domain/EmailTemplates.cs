using System.Net;
using System.Text.RegularExpressions;
using Ctx.Notification.Contract.V1;

namespace Ctx.Notification.Domain;

/// <summary>A rendered email: the subject (as LazyTax) and an HTML body.</summary>
public sealed record RenderedEmail(string Subject, string Html);

/// <summary>
/// The nine LazyTax emails as small HTML bodies (LazyTax builds them from React Email; this port keeps the
/// subjects and the variables, not the styling). <c>{{Name}}</c> is replaced by the HTML-escaped variable.
/// A variable the template needs but did not get fails the render (<see cref="MissingVariable"/>), so a
/// <c>{{</c> never reaches a user.
/// </summary>
public static partial class EmailTemplates
{
  private const string Footer = "<p>Questions? Write to <a href=\"mailto:{{SupportEmail}}\">{{SupportEmail}}</a>.</p>";

  private static readonly Dictionary<string, (string Subject, string Body)> Templates = new()
  {
    [EmailTemplateIds.PenaltyCharged] = ("Your LazyTax habit stake receipt",
      "<p>Hi {{UserName}},</p><p>You missed <b>{{HabitName}}</b>, so your stake of <b>{{Amount}}</b> was charged on {{ChargeDate}} and goes to {{CharityName}}.</p>"),
    [EmailTemplateIds.PenaltyPaymentFailed] = ("We couldn't process your stake",
      "<p>Hi {{UserName}},</p><p>You missed <b>{{HabitName}}</b>, but we couldn't charge your stake of <b>{{Amount}}</b> for {{CharityName}} on {{AttemptDate}}. Check your payment method in <a href=\"{{BaseUrl}}\">LazyTax</a>.</p>"),
    [EmailTemplateIds.SubscriptionReceipt] = ("Your LazyTax receipt",
      "<p>Hi {{UserName}},</p><p>Thanks for being on the <b>{{Tier}}</b> plan. We charged {{Amount}} on {{ChargeDate}}. Your next billing date is {{NextBillingDate}}.</p>"),
    [EmailTemplateIds.SubscriptionPaymentFailed] = ("Payment failed — your plan needs attention",
      "<p>Hi {{UserName}},</p><p>We couldn't charge {{Amount}} for your <b>{{Tier}}</b> plan. Update your payment method in <a href=\"{{BaseUrl}}\">LazyTax</a> by {{GraceEndsDate}} to keep it.</p>"),
    [EmailTemplateIds.SubscriptionCancelled] = ("Your subscription change is confirmed",
      "<p>Hi {{UserName}},</p><p>Your subscription change is confirmed: <b>{{ChangeSummary}}</b>, effective {{EffectiveDate}}.</p>"),
    [EmailTemplateIds.Welcome] = ("Welcome to LazyTax — let's build habits that stick",
      "<p>Hi {{UserName}},</p><p>Welcome to <a href=\"{{BaseUrl}}\">LazyTax</a>. Pick a habit, stake a little, and let us keep you honest.</p>"),
    [EmailTemplateIds.PaymentConsentChanged] = ("Your payment method changed",
      "<p>Hi {{UserName}},</p><p>{{ChangeSummary}} for <b>{{Purpose}}</b> on {{ChangeDate}}. If this wasn't you, contact us at once.</p>"),
    [EmailTemplateIds.MemberThankYou] = ("Thank you for being a LazyTax member",
      "<p>Hi {{UserName}},</p><p>Thank you for being a {{MembershipType}} member since {{MemberSince}}.</p>"),
    [EmailTemplateIds.VerificationCode] = ("Your LazyTax verification code",
      "<p>Hi {{UserName}},</p><p>Your verification code is <b>{{Code}}</b>.</p>"),
  };

  public static bool Exists(string template) => Templates.ContainsKey(template);

  /// <summary>Renders <paramref name="template"/>; throws <see cref="MissingVariable"/> or <see cref="ArgumentException"/> (unknown template).</summary>
  public static RenderedEmail Render(string template, IReadOnlyDictionary<string, string> vars)
  {
    if (!Templates.TryGetValue(template, out var t)) throw new ArgumentException($"unknown email template '{template}'", nameof(template));
    var html = Token().Replace("<html><body>" + t.Body + Footer + "</body></html>", m =>
    {
      var name = m.Groups[1].Value;
      return vars.TryGetValue(name, out var v) ? WebUtility.HtmlEncode(v) : throw new MissingVariable(template, name);
    });
    return new RenderedEmail(t.Subject, html);
  }

  [GeneratedRegex(@"\{\{(\w+)\}\}")]
  private static partial Regex Token();
}

public sealed class MissingVariable(string template, string variable)
  : Exception($"email '{template}' needs the variable '{variable}'");
