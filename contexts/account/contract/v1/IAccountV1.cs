using LazyTax.Platform.Data;

namespace Ctx.Account.Contract.V1;

/// <summary>account.v1: users and their settings (LazyTax Users + Configurations).</summary>
public interface IAccountV1
{
  /// <summary>The owner's user profile, or null when the user does not exist. <see cref="Owner.Anyone"/> is refused.</summary>
  Task<AccountProfile?> GetProfile(Owner owner, CancellationToken ct = default);

  /// <summary>True when the user row exists.</summary>
  Task<bool> Exists(string userId, CancellationToken ct = default);

  /// <summary>The user's IANA timezone, or null when they have no configuration yet.</summary>
  Task<string?> GetTimezone(string userId, CancellationToken ct = default);

  /// <summary>The user's end of day (LazyTax stores 23:59 for everyone).</summary>
  Task<TimeOnly> EndOfDay(string userId, CancellationToken ct = default);

  /// <summary>The user's default charity, or null when none is picked.</summary>
  Task<Guid?> DefaultCharity(string userId, CancellationToken ct = default);

  /// <summary>The user's local date now (LazyTax's AllowanceService.GetUserToday). 404 when they have no configuration.</summary>
  Task<UserToday> Today(string userId, DateTime? nowUtc = null, CancellationToken ct = default);

  /// <summary>
  /// Users whose username starts with <paramref name="prefix"/> (case-insensitive; %, _ and \ match literally),
  /// excluding <paramref name="excludeUserId"/>, ordered by lower(username), username, id. At most <paramref name="limit"/> (1-100).
  /// </summary>
  /// Added after the freeze: the default body keeps existing fakes compiling; AccountV1 implements it.
  Task<IReadOnlyList<UsernameRef>> SearchUsernames(string prefix, string excludeUserId, int limit, CancellationToken ct = default) =>
    throw new NotSupportedException("SearchUsernames");

  /// <summary>Username by user id, for the ids that exist.</summary>
  Task<IReadOnlyDictionary<string, string>> Usernames(IReadOnlyCollection<string> userIds, CancellationToken ct = default) =>
    throw new NotSupportedException("Usernames");
}

public sealed record AccountProfile(string Id, string Username, string Email, bool EmailVerified, bool Active);

public sealed record UserToday(string Timezone, DateOnly Today);

public sealed record UsernameRef(string UserId, string Username);
