namespace Ctx.Charity.Api.V1;

// Same JSON shapes as LazyTax's CharityModel.cs / CauseModel.cs.
public sealed record CreateCharityReq(
  string Name, string? Slug, string? Mission, string[]? Countries,
  string? PrimaryRegistrationNumber, string? PrimaryRegistrationCountry, string? WebsiteUrl, string? LogoUrl);

public sealed record UpdateCharityReq(
  string Name, string? Slug, string? Mission, string[]? Countries,
  string? PrimaryRegistrationNumber, string? PrimaryRegistrationCountry, string? WebsiteUrl, string? LogoUrl);

public sealed record CharitySearchReq(
  string? Name, string? Slug, string? Country, string? PrimaryRegistrationNumber,
  string? PrimaryRegistrationCountry, string? CauseKey, int? Limit, int? Skip);

public sealed record CharityPrincipalRes(
  string Id, string Name, string? Slug, string? Mission, string[] Countries,
  string? PrimaryRegistrationNumber, string? PrimaryRegistrationCountry, string? WebsiteUrl, string? LogoUrl);

public sealed record CharityRes(CharityPrincipalRes Principal);

public sealed record SetCharityCausesReq(string[] Keys);

public sealed record CreateCauseReq(string Key, string Name);

public sealed record UpdateCauseReq(string Name);

public sealed record CauseSearchReq(string? Key, string? Name, int? Limit, int? Skip);

public sealed record CausePrincipalRes(string Id, string Key, string Name);

public sealed record CauseRes(CausePrincipalRes Principal);

public sealed record PledgeSyncSummaryRes(int CausesUpserted, int CharitiesCreated, int CharitiesUpdated, int ExternalIdsLinked, int CharitiesProcessed);
