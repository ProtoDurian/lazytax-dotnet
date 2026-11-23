namespace Ctx.Charity.Domain;

public sealed record CharityRecord(
  string Name,
  string? Slug,
  string? Mission,
  string[]? Countries,
  string? PrimaryRegistrationNumber,
  string? PrimaryRegistrationCountry,
  string? WebsiteUrl,
  string? LogoUrl);

public sealed record CharityModel(Guid Id, CharityRecord Record);

public sealed record CauseRecord(string Key, string Name);

public sealed record CauseModel(Guid Id, CauseRecord Record);

public sealed record CauseSearch(string? Key, string? Name, int Limit, int Skip);

public sealed record ExternalIdRecord(string Source, string ExternalKey, string? Url, string? Payload, DateTimeOffset? LastSyncedAt);

public sealed record BulkUpsertCharity(CharityRecord Charity, ExternalIdRecord ExternalId, string[] CauseKeys);

public sealed record BulkUpsertResult(int CharitiesCreated, int CharitiesUpdated, int ExternalIdsLinked, int CausesLinked);

public sealed record PledgeSyncSummary(int CausesUpserted, int CharitiesCreated, int CharitiesUpdated, int ExternalIdsLinked, int CharitiesProcessed);
