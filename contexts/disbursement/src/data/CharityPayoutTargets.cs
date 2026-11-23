using Ctx.Charity.Contract.V1;
using Ctx.Disbursement.Domain;

namespace Ctx.Disbursement.Data;

/// <summary>Finds a charity's Pledge organisation through charity.v1 (LazyTax read <c>ExternalIds</c> directly).</summary>
public sealed class CharityPayoutTargets(ICharityV1 charity) : IPayoutTargets
{
  private const string PledgeSource = "pledge";

  public Task<string?> PledgeOrganizationId(Guid charityId, CancellationToken ct = default) =>
    charity.GetExternalKey(charityId, PledgeSource, ct);
}
