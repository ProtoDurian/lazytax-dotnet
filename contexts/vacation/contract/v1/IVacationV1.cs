namespace Ctx.Vacation.Contract.V1;

/// <summary>vacation.v1: whether a user is away on a day (check settles a missed day as "vacation").</summary>
public interface IVacationV1
{
  /// <summary>True when one of the user's vacation periods covers <paramref name="date"/> (a user-local day).</summary>
  Task<bool> IsAway(string userId, DateOnly date, CancellationToken ct = default);

  /// <summary>The user's vacation periods that cover <paramref name="date"/>.</summary>
  Task<IReadOnlyList<VacationPeriod>> ListActive(string userId, DateOnly date, CancellationToken ct = default);
}

public sealed record VacationPeriod(Guid Id, string UserId, DateOnly StartDate, DateOnly EndDate, string Timezone);
