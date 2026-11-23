using Ctx.Charity.Contract.V1;
using Ctx.Charity.Domain;

namespace Ctx.Charity.Api.V1;

public static class Mapper
{
  public static CharityPrincipalRes ToRes(this CharityModel c) => new(
    c.Id.ToString(), c.Record.Name, c.Record.Slug, c.Record.Mission, c.Record.Countries ?? [],
    c.Record.PrimaryRegistrationNumber, c.Record.PrimaryRegistrationCountry, c.Record.WebsiteUrl, c.Record.LogoUrl);

  public static CharityRecord ToRecord(this CreateCharityReq r) => new(
    r.Name, r.Slug, r.Mission, r.Countries, r.PrimaryRegistrationNumber, r.PrimaryRegistrationCountry, r.WebsiteUrl, r.LogoUrl);

  public static CharityRecord ToRecord(this UpdateCharityReq r) => new(
    r.Name, r.Slug, r.Mission, r.Countries, r.PrimaryRegistrationNumber, r.PrimaryRegistrationCountry, r.WebsiteUrl, r.LogoUrl);

  public static CharitySearchQuery ToQuery(this CharitySearchReq r) => new()
  {
    Name = r.Name,
    Slug = r.Slug,
    Country = r.Country,
    PrimaryRegistrationNumber = r.PrimaryRegistrationNumber,
    PrimaryRegistrationCountry = r.PrimaryRegistrationCountry,
    CauseKey = r.CauseKey,
    Limit = r.Limit ?? 20,
    Skip = r.Skip ?? 0,
  };

  public static CausePrincipalRes ToRes(this CauseModel c) => new(c.Id.ToString(), c.Record.Key, c.Record.Name);

  public static CauseSearch ToSearch(this CauseSearchReq r) => new(r.Key, r.Name, r.Limit ?? 20, r.Skip ?? 0);
}
