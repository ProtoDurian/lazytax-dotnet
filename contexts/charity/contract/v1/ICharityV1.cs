namespace Ctx.Charity.Contract.V1;

/// <summary>charity.v1: the charity and cause catalogue. It is global data (no owner), so reads take no <c>Owner</c>.</summary>
public interface ICharityV1
{
  /// <summary>The charity, or null when none has this id.</summary>
  Task<CharityView?> Get(Guid id, CancellationToken ct = default);

  /// <summary>True when a charity with this id exists.</summary>
  Task<bool> Exists(Guid id, CancellationToken ct = default);

  /// <summary>Charities matching the query, paged.</summary>
  Task<IReadOnlyList<CharityView>> Search(CharitySearchQuery q, CancellationToken ct = default);

  /// <summary>The charity linked to an external source (for example <c>pledge</c> and the organisation id), or null.</summary>
  Task<CharityView?> GetByExternalId(string source, string externalKey, CancellationToken ct = default);

  /// <summary>The latest-synced external key of the charity for a source such as <c>pledge</c>; null when none.</summary>
  Task<string?> GetExternalKey(Guid charityId, string source, CancellationToken ct = default) => Task.FromResult<string?>(null);
}

public sealed record CharityView(
  Guid Id,
  string Name,
  string? Slug,
  string? Mission,
  string[] Countries,
  string? PrimaryRegistrationNumber,
  string? PrimaryRegistrationCountry,
  string? WebsiteUrl,
  string? LogoUrl);

public sealed record CharitySearchQuery
{
  public string? Name { get; init; }
  public string? Slug { get; init; }
  public string? Country { get; init; }
  public string? PrimaryRegistrationNumber { get; init; }
  public string? PrimaryRegistrationCountry { get; init; }
  public string? CauseKey { get; init; }
  public int Limit { get; init; } = 20;
  public int Skip { get; init; }
}
