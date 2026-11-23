namespace Ctx.Vacation.Domain;

public sealed record VacationRecord(DateOnly StartDate, DateOnly EndDate, string Timezone);

public sealed record VacationPrincipal(Guid Id, string UserId, VacationRecord Record, DateTime CreatedAt);

public sealed record VacationSearch(int? Year, int Limit, int Skip);
