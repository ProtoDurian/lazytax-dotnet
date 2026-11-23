using System.Globalization;
using Ctx.Vacation.Domain;
using LazyTax.Platform.Problems;

namespace Ctx.Vacation.Api.V1;

public sealed record CreateVacationReq(string StartDate, string EndDate, string Timezone);

public sealed record VacationRes(Guid Id, string UserId, string StartDate, string EndDate, string Timezone, string CreatedAt);

public sealed record SearchVacationQuery(int? Year, int? Limit, int? Skip);

public static class Mapper
{
  private const string DateFormat = "dd-MM-yyyy"; // LazyTax's standard date format

  public static VacationRecord ToRecord(this CreateVacationReq r)
  {
    var errors = new Dictionary<string, string[]>();
    if (!TryDate(r.StartDate, out var start)) errors["StartDate"] = [$"StartDate must be a date ({DateFormat})"];
    if (!TryDate(r.EndDate, out var end)) errors["EndDate"] = [$"EndDate must be a date ({DateFormat})"];
    if (string.IsNullOrWhiteSpace(r.Timezone) || !TimeZoneInfo.TryFindSystemTimeZoneById(r.Timezone, out _))
      errors["Timezone"] = ["Timezone must be a valid IANA timezone"];
    if (errors.Count > 0) throw new ValidationFailed("Invalid CreateVacationReq", errors);
    return new VacationRecord(start, end, r.Timezone);
  }

  public static VacationSearch ToDomain(this SearchVacationQuery q)
  {
    var limit = q.Limit ?? 50;
    var skip = q.Skip ?? 0;
    if (limit is < 1 or > 100 || skip < 0)
      throw new ValidationFailed("Invalid SearchVacationQuery", "Limit/Skip", "Limit must be 1 to 100 and Skip at least 0");
    return new VacationSearch(q.Year, limit, skip);
  }

  public static VacationRes ToRes(this VacationPrincipal p) => new(p.Id, p.UserId,
    p.Record.StartDate.ToString(DateFormat, CultureInfo.InvariantCulture),
    p.Record.EndDate.ToString(DateFormat, CultureInfo.InvariantCulture),
    p.Record.Timezone, p.CreatedAt.ToString("O"));

  private static bool TryDate(string? s, out DateOnly d) =>
    DateOnly.TryParseExact(s, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
}
