namespace Ctx.Account.Domain;

public sealed record UserRecord(string Username, string Email, bool EmailVerified, bool Active, string[] Scopes);

public sealed record UserPrincipal(string Id, UserRecord Record);

public sealed record UserSearch(string? Id, string? Username, string? Email, bool? EmailVerified, bool? Active, int Limit, int Skip);

public sealed record ConfigurationRecord(string Timezone, Guid? DefaultCharityId);

public sealed record ConfigurationPrincipal(Guid Id, string UserId, ConfigurationRecord Record);
