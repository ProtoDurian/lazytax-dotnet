using Ctx.Account.Contract.V1;
using Ctx.Plan.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;

namespace Ctx.Vacation.Domain;

/// <summary>LazyTax's VacationService. The window quota is counted and written under the user lock.</summary>
public sealed class VacationService(IVacationRepository repo, IPlanV1 plan, IAccountV1 account, IUnitOfWork uow)
{
  public Task<VacationPrincipal> Create(string userId, VacationRecord record, CancellationToken ct)
  {
    if (record.StartDate > record.EndDate)
      throw new ValidationFailed("Invalid CreateVacationReq", "StartDate/EndDate", "StartDate must be before or equal to EndDate");
    var owner = Owner.User(userId);
    return uow.Start(async () =>
    {
      // Layer 5, step 3: lock first, then count, then write, in one transaction.
      await uow.LockUser(userId, ct);
      if (await repo.HasOverlap(owner, record.StartDate, record.EndDate, ct))
        throw new ValidationFailed("Invalid CreateVacationReq", "Range", "Vacation window overlaps with an existing one");
      var windows = await plan.Quota(userId, QuotaKind.VacationWindows, ct);
      // A vacation uses one window, in the year it starts, even when it spans New Year.
      if (await repo.CountWindowsForYear(owner, record.StartDate.Year, ct) >= windows.Limit)
        throw new TierInsufficient(windows.Tier, windows.Key, windows.Limit);
      return await repo.Create(userId, record, ct);
    }, ct);
  }

  public Task<IReadOnlyList<VacationPrincipal>> Search(Owner owner, VacationSearch search, CancellationToken ct) =>
    repo.Search(owner, search, ct);

  public Task Delete(string userId, Guid id, CancellationToken ct) =>
    uow.Start(async () =>
    {
      var v = await repo.Get(Owner.User(userId), id, ct) ?? throw new NotFound("Vacation", id.ToString());
      // Vacation dates are user-local days, in the user's current timezone like every check.
      var today = (await account.Today(userId, null, ct)).Today;
      if (today >= v.Record.StartDate)
        throw new ValidationFailed("Invalid DeleteVacation", "State", "Cannot delete a vacation that has started");
      await repo.Delete(Owner.User(userId), id, ct);
    }, ct);

  public Task<VacationPrincipal> EndToday(string userId, Guid id, CancellationToken ct) =>
    uow.Start(async () =>
    {
      var v = await repo.Get(Owner.User(userId), id, ct) ?? throw new NotFound("Vacation", id.ToString());
      var today = (await account.Today(userId, null, ct)).Today;
      if (today < v.Record.StartDate)
        throw new ValidationFailed("Invalid EndVacation", "State", "Cannot end a vacation that hasn't started");
      var record = v.Record with { EndDate = today < v.Record.EndDate ? today : v.Record.EndDate };
      return await repo.Update(Owner.User(userId), id, record, ct) ?? throw new NotFound("Vacation", id.ToString());
    }, ct);
}
