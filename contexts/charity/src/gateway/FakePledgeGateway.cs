using Ctx.Charity.Domain;

namespace Ctx.Charity.Gateway;

/// <summary>
/// Pledge with no network: a small fixed catalogue (two causes, one a child of the other; two organisations in
/// US and GB). LazyTax's real client is an HTTP client; here every gateway is a fake, and none runs in a transaction.
/// </summary>
public sealed class FakePledgeGateway : IPledgeGateway
{
  private static readonly PledgeCause[] Causes = [new(1, "Health", null), new(2, "Cancer", 1)];

  private static readonly PledgeOrganization[] Orgs =
  [
    new("org-us-1", "Fake Hope Fund", "11-1111111", "Helps people.", "US", "https://hope.example/", "https://pledge.example/hope", "https://hope.example/logo.png"),
    new("org-gb-1", "Fake Care Trust", "GB-222", "Cares.", "GB", "https://care.example/", "https://pledge.example/care", ""),
  ];

  public Task<IReadOnlyList<PledgeCause>> GetCauses(CancellationToken ct) => Task.FromResult<IReadOnlyList<PledgeCause>>(Causes);

  public Task<IReadOnlyList<PledgeOrganization>> GetOrganizations(string country, string causeKey, int perPage, CancellationToken ct) =>
    Task.FromResult<IReadOnlyList<PledgeOrganization>>(
      causeKey == "2" ? Orgs.Where(o => o.Country == country).Take(perPage).ToArray() : []);
}
