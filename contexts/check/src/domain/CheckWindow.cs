namespace Ctx.Check.Domain;

/// <summary>A check's window in a timezone (LazyTax Domain/Check/CheckWindow.cs, unchanged).</summary>
public static class CheckWindow
{
  public static readonly TimeOnly DefaultDeadline = new(23, 59);

  /// <summary>
  /// Late evidence that happened before the deadline can still land this long after it, and the tick waits
  /// this long before settling a check as missed.
  /// </summary>
  public static readonly TimeSpan Grace = TimeSpan.FromMinutes(30);

  public static TimeZoneInfo Resolve(string? timezoneId)
  {
    if (!string.IsNullOrWhiteSpace(timezoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timezoneId, out var tz))
      return tz;
    return TimeZoneInfo.Utc;
  }

  public static bool IsKnown(string? timezoneId) =>
    !string.IsNullOrWhiteSpace(timezoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timezoneId, out _);

  public static DateOnly LocalDate(DateTime utc, TimeZoneInfo tz) =>
    DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz));

  /// <summary>
  /// A check opens at local midnight and its deadline is the end of the deadline minute (23:59 means "until
  /// midnight"), both in the given timezone.
  /// </summary>
  public static (DateTime OpensAtUtc, DateTime DeadlineUtc) For(DateOnly date, TimeZoneInfo tz, TimeOnly deadline)
  {
    var opens = ToUtc(date.ToDateTime(TimeOnly.MinValue), tz);
    var due = ToUtc(date.ToDateTime(deadline), tz).AddMinutes(1);
    if (due <= opens) due = opens.AddMinutes(1);
    return (opens, due);
  }

  /// <summary>The user-local month that holds <paramref name="today"/> (LazyTax AllowanceService.GetUserMonthWindow).</summary>
  public static (DateOnly Start, DateOnly End) MonthOf(DateOnly today)
  {
    var start = new DateOnly(today.Year, today.Month, 1);
    return (start, start.AddMonths(1).AddDays(-1));
  }

  private static DateTime ToUtc(DateTime local, TimeZoneInfo tz)
  {
    // Local times inside a DST gap don't exist; move forward to the first valid time.
    var l = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    while (tz.IsInvalidTime(l)) l = l.AddMinutes(15);
    return TimeZoneInfo.ConvertTimeToUtc(l, tz);
  }
}
