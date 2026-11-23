using LazyTax.Platform.Problems;

namespace Ctx.Charity.Domain;

/// <summary>Port of LazyTax's FluentValidation rules (CharityValidator, CauseValidator) as plain checks.</summary>
public static class Validation
{
  private static void Fail(string detail, string field, string error) => throw new ValidationFailed(detail, field, error);

  private static void Name(string field, string? v)
  {
    if (v is null || v.Length is < 1 or > 256) Fail("Invalid " + field, field, "Name has to be between 1 to 256 characters");
  }

  private static void Url(string field, string? v)
  {
    if (v is null) return;
    if (!Uri.TryCreate(v, UriKind.Absolute, out _)) Fail("Invalid " + field, field, "Must be a valid URL");
  }

  public static CharityRecord Charity(CharityRecord r)
  {
    Name("name", r.Name);
    if (r.Slug is { Length: > 128 }) Fail("Invalid slug", "slug", "Slug is too long");
    if (r.Mission is { Length: > 8192 }) Fail("Invalid mission", "mission", "Mission is too long");
    if (r.Countries is not null && r.Countries.Any(c => string.IsNullOrWhiteSpace(c) || c.Length != 2))
      Fail("Invalid countries", "countries", "Each country must be an ISO alpha-2 code");
    if (r.PrimaryRegistrationNumber is { Length: > 128 }) Fail("Invalid number", "primaryRegistrationNumber", "Too long");
    if (r.PrimaryRegistrationCountry is { Length: not 2 }) Fail("Invalid country", "primaryRegistrationCountry", "Must be 2 characters");
    Url("websiteUrl", r.WebsiteUrl);
    Url("logoUrl", r.LogoUrl);
    return r;
  }

  public static void Paging(int limit, int skip)
  {
    if (limit is < 0 or > 100) Fail("Invalid limit", "limit", "Limit has to be between 0 to 100 characters");
    if (skip < 0) Fail("Invalid skip", "skip", "Skip has to be larger than or equal to 0");
  }

  public static void SearchCountries(string? country, string? regCountry)
  {
    if (country is { Length: not 2 }) Fail("Invalid country", "country", "Must be 2 characters");
    if (regCountry is { Length: not 2 }) Fail("Invalid country", "primaryRegistrationCountry", "Must be 2 characters");
  }

  public static void CauseKeys(string[]? keys)
  {
    if (keys is null) Fail("Invalid keys", "keys", "Keys must not be null");
    foreach (var k in keys!)
      if (string.IsNullOrEmpty(k) || k.Length > 128) Fail("Invalid keys", "keys", "Each key must be 1 to 128 characters");
  }

  public static void Cause(CauseRecord r, bool keyRequired)
  {
    if (keyRequired && (string.IsNullOrEmpty(r.Key) || r.Key.Length > 128)) Fail("Invalid key", "key", "Key must be 1 to 128 characters");
    if (string.IsNullOrEmpty(r.Name) || r.Name.Length > 256) Fail("Invalid name", "name", "Name has to be between 1 to 256 characters");
  }

  public static void CauseSearch(CauseSearch s)
  {
    if (s.Key is { Length: > 128 }) Fail("Invalid key", "key", "Too long");
    if (s.Name is { Length: > 256 }) Fail("Invalid name", "name", "Too long");
    Paging(s.Limit, s.Skip);
  }
}
