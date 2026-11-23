namespace Ctx.Account.Domain;

/// <summary>User-local dates (LazyTax AllowanceService.GetUserToday).</summary>
public static class Timezones
{
  public static bool IsValid(string? tz) => !string.IsNullOrWhiteSpace(tz) && tz.Length <= 64 && TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _);

  public static DateOnly Today(string tz, DateTime nowUtc) =>
    DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(tz)));
}
