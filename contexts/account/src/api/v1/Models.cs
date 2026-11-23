using Ctx.Account.Domain;

namespace Ctx.Account.Api.V1;

public sealed record SearchUserQuery(string? Id, string? Username, string? Email, bool? EmailVerified, bool? Active, int? Limit, int? Skip);

public sealed record CreateUserReq(string IdToken, string AccessToken);

public sealed record UpdateUserReq(string IdToken, string AccessToken);

public sealed record UserPrincipalRes(string Id, string Username, string Email, bool EmailVerified, bool Active);

public sealed record UserRes(UserPrincipalRes Principal);

// DefaultCharityId is optional (null or omitted: none). An update without it clears the default.
public sealed record CreateConfigurationReq(string Timezone, string? DefaultCharityId = null);

public sealed record UpdateConfigurationReq(string Timezone, string? DefaultCharityId = null);

public sealed record ConfigurationPrincipalRes(string Id, string UserId, string Timezone, string? DefaultCharityId);

public sealed record TimezoneSettledCheckRes(string HabitId, string Date, string Status);

public sealed record UpdateConfigurationRes(string Id, string UserId, string Timezone, string? DefaultCharityId, List<TimezoneSettledCheckRes> SettledChecks);

// Charity is another context's data; account no longer joins it (always null).
public sealed record ConfigurationRes(ConfigurationPrincipalRes Principal, object? Charity);

public static class Mapper
{
  public static UserPrincipalRes ToRes(this UserPrincipal u) => new(u.Id, u.Record.Username, u.Record.Email, u.Record.EmailVerified, u.Record.Active);

  public static UserSearch ToDomain(this SearchUserQuery q)
  {
    var limit = q.Limit ?? 20;
    var skip = q.Skip ?? 0;
    if (limit is < 1 or > 100 || skip < 0)
      throw new LazyTax.Platform.Problems.ValidationFailed("Invalid SearchUserQuery", "Limit/Skip", "Limit must be 1 to 100 and Skip at least 0");
    return new UserSearch(Blank(q.Id), Blank(q.Username), Blank(q.Email), q.EmailVerified, q.Active, limit, skip);
  }

  public static ConfigurationPrincipalRes ToRes(this ConfigurationPrincipal c) =>
    new(c.Id.ToString(), c.UserId, c.Record.Timezone, c.Record.DefaultCharityId?.ToString());

  public static ConfigurationRecord ToRecord(string timezone, string? defaultCharityId)
  {
    if (string.IsNullOrEmpty(defaultCharityId)) return new ConfigurationRecord(timezone, null);
    return Guid.TryParse(defaultCharityId, out var id)
      ? new ConfigurationRecord(timezone, id)
      : throw new LazyTax.Platform.Problems.ValidationFailed("Invalid Configuration", "DefaultCharityId", "DefaultCharityId must be a valid GUID");
  }

  private static string? Blank(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
