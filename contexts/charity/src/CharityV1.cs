using Ctx.Charity.Contract.V1;
using Ctx.Charity.Domain;

namespace Ctx.Charity;

/// <summary>charity.v1, as other contexts see the catalogue.</summary>
internal sealed class CharityV1(CharityService service) : ICharityV1
{
  private static CharityView ToView(CharityModel c) => new(
    c.Id, c.Record.Name, c.Record.Slug, c.Record.Mission, c.Record.Countries ?? [],
    c.Record.PrimaryRegistrationNumber, c.Record.PrimaryRegistrationCountry, c.Record.WebsiteUrl, c.Record.LogoUrl);

  public async Task<CharityView?> Get(Guid id, CancellationToken ct = default) =>
    await service.Find(id, ct) is { } c ? ToView(c) : null;

  public Task<bool> Exists(Guid id, CancellationToken ct = default) => service.Exists(id, ct);

  public async Task<IReadOnlyList<CharityView>> Search(CharitySearchQuery q, CancellationToken ct = default) =>
    [.. (await service.Search(q, ct)).Select(ToView)];

  public async Task<CharityView?> GetByExternalId(string source, string externalKey, CancellationToken ct = default) =>
    await service.GetByExternalId(source, externalKey, ct) is { } c ? ToView(c) : null;

  public Task<string?> GetExternalKey(Guid charityId, string source, CancellationToken ct = default) =>
    service.GetExternalKey(charityId, source, ct);
}
