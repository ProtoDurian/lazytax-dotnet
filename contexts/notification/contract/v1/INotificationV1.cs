namespace Ctx.Notification.Contract.V1;

/// <summary>notification.v1: the user's in-app inbox and transactional email.</summary>
public interface INotificationV1
{
  /// <summary>
  /// Puts a notification in the user's inbox (LazyTax <c>IInboxService.Post</c>). Joins the caller's unit of work.
  /// Title 1..256 chars and body 1..1024 (else 400). Posting the same <paramref name="dedupeKey"/> to the same
  /// user twice records it once and returns the first (outbox retries).
  /// </summary>
  Task<InboxNotification> Post(
    string userId,
    string kind,
    string title,
    string body,
    string? actorUserId = null,
    string? data = null,
    string? dedupeKey = null,
    CancellationToken ct = default);

  /// <summary>
  /// Asks for an email to the user's registered address. Never throws for a delivery problem: the request is an
  /// outbox event in the caller's unit of work and the send happens after commit. <paramref name="data"/> holds
  /// the template's variables as strings; <c>UserName</c>, <c>BaseUrl</c> and <c>SupportEmail</c> are filled in.
  /// </summary>
  Task Email(string userId, string template, IReadOnlyDictionary<string, string>? data = null, CancellationToken ct = default);

  /// <summary>How many inbox notifications the user has not read.</summary>
  Task<int> UnreadCount(string userId, CancellationToken ct = default);
}

/// <summary>One inbox row. <paramref name="Data"/> is the JSON text the app uses for the tap action.</summary>
public sealed record InboxNotification(
  Guid Id,
  string UserId,
  string Kind,
  string Title,
  string Body,
  string? ActorUserId,
  string? Data,
  DateTime CreatedAtUtc,
  DateTime? ReadAtUtc);

/// <summary>What a notification is about; the app picks its icon and tap action from this.</summary>
public static class InboxKinds
{
  public const string FriendRequest = "friend_request";
  public const string FriendAccepted = "friend_accepted";
  public const string FriendMissed = "friend_missed";
  public const string PenaltyCharged = "penalty_charged";
  public const string PenaltyFailed = "penalty_failed";
}

/// <summary>The email templates (LazyTax <c>EmailTemplates</c>).</summary>
public static class EmailTemplateIds
{
  public const string PenaltyCharged = "penalty-charged";
  public const string PenaltyPaymentFailed = "penalty-payment-failed";
  public const string SubscriptionReceipt = "subscription-receipt";
  public const string SubscriptionPaymentFailed = "subscription-payment-failed";
  public const string SubscriptionCancelled = "subscription-cancelled";
  public const string Welcome = "welcome";
  public const string PaymentConsentChanged = "payment-consent-changed";
  public const string MemberThankYou = "member-thank-you";
  public const string VerificationCode = "verification-code";

  public static readonly IReadOnlyList<string> All =
  [
    PenaltyCharged, PenaltyPaymentFailed, SubscriptionReceipt, SubscriptionPaymentFailed,
    SubscriptionCancelled, Welcome, PaymentConsentChanged, MemberThankYou, VerificationCode,
  ];
}
