namespace Ctx.Account.Contract.V1;

/// <summary>Outbox event names emitted by account.</summary>
public static class AccountEvents
{
  /// <summary>
  /// The user's timezone changed (payload <see cref="TimezoneChanged"/>). check subscribes to move pending
  /// checks (LazyTax called checkService.MoveToTimezone synchronously).
  /// </summary>
  public const string TimezoneChanged = "account.timezone_changed";

  /// <summary>A user row was created (payload <see cref="UserCreated"/>): welcome email, Logto claim sync.</summary>
  public const string UserCreated = "account.user_created";

  /// <summary>A user was purged from every context (payload <see cref="UserDeleted"/>): Logto identity removal.</summary>
  public const string UserDeleted = "account.user_deleted";
}

public sealed record TimezoneChanged(string UserId, string? From, string To);

public sealed record UserCreated(string UserId);

public sealed record UserDeleted(string UserId);
