using LazyTax.Platform.Data;

namespace Ctx.Vacation.Domain;

/// <summary>Vacation periods. Every read takes an <see cref="Owner"/>.</summary>
public interface IVacationRepository
{
  Task<VacationPrincipal?> Get(Owner owner, Guid id, CancellationToken ct);
  Task<IReadOnlyList<VacationPrincipal>> Search(Owner owner, VacationSearch search, CancellationToken ct);
  Task<IReadOnlyList<VacationPrincipal>> ListActive(Owner owner, DateOnly date, CancellationToken ct);
  Task<int> CountWindowsForYear(Owner owner, int year, CancellationToken ct);
  Task<bool> HasOverlap(Owner owner, DateOnly start, DateOnly end, CancellationToken ct);
  Task<VacationPrincipal> Create(string userId, VacationRecord record, CancellationToken ct);
  Task<VacationPrincipal?> Update(Owner owner, Guid id, VacationRecord record, CancellationToken ct);
  Task<bool> Delete(Owner owner, Guid id, CancellationToken ct);
}
