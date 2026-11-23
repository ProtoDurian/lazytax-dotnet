using Ctx.Charity.Contract.V1;
using Ctx.Penalty.Domain;

namespace Ctx.Penalty.Adapters;

/// <summary>charity.v1 -> the charity name carried on penalty.charged / penalty.failed for the email.</summary>
internal sealed class CharityNames(ICharityV1 charity) : ICharityNames
{
  public async Task<string?> NameOf(Guid charityId, CancellationToken ct) => (await charity.Get(charityId, ct))?.Name;
}
