namespace Ctx.Notification.Contract.V1;

/// <summary>Outbox event names emitted by notification.</summary>
public static class NotificationEvents
{
  /// <summary>An email waiting to leave (payload <see cref="EmailRequested"/>); notification's own subscriber sends it.</summary>
  public const string EmailRequested = "notification.email_requested";
}

public sealed record EmailRequested(string UserId, string Template, Dictionary<string, string> Data);
