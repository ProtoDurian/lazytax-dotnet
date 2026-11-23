using Ctx.Vacation.Contract.V1;
using Ctx.Vacation.Domain;
using LazyTax.Platform.Data;

namespace Ctx.Vacation;

/// <summary>vacation.v1 for other contexts (check settles a missed day as "vacation").</summary>
public sealed class VacationV1(IVacationRepository repo) : IVacationV1
{
  public async Task<bool> IsAway(string userId, DateOnly date, CancellationToken ct = default) =>
    (await repo.ListActive(Owner.User(userId), date, ct)).Count > 0;

  public async Task<IReadOnlyList<VacationPeriod>> ListActive(string userId, DateOnly date, CancellationToken ct = default) =>
    [.. (await repo.ListActive(Owner.User(userId), date, ct)).Select(v =>
      new VacationPeriod(v.Id, v.UserId, v.Record.StartDate, v.Record.EndDate, v.Record.Timezone))];
}
