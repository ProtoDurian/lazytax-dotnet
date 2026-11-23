using Ctx.Protection.Contract.V1;
using LazyTax.Platform.Data;

namespace Ctx.Protection.Domain;

/// <summary>protection.v1: a thin face over <see cref="ProtectionService"/>.</summary>
public sealed class ProtectionV1(ProtectionService service) : IProtectionV1
{
  public Task<FreezeBalance> ReconcileCap(string userId, int maxStreak, CancellationToken ct = default) => service.ReconcileCap(userId, maxStreak, ct);
  public Task<bool> TryConsumeFreeze(string userId, DateOnly date, CancellationToken ct = default) => service.TryConsumeFreeze(userId, date, ct);
  public Task<FreezeBalance> Balance(Owner owner, CancellationToken ct = default) => service.Balance(owner, ct);
}
