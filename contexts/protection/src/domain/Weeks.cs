namespace Ctx.Protection.Domain;

/// <summary>Calendar-date helpers (LazyTax <c>StreakCalculator.WeekSundayBounds</c>). Pure.</summary>
public static class Weeks
{
  /// <summary>The Sunday-to-Saturday week that contains <paramref name="day"/>.</summary>
  public static (DateOnly Start, DateOnly End) SundayBounds(DateOnly day)
  {
    var start = day.AddDays(-(int)day.DayOfWeek); // Sunday = 0
    return (start, start.AddDays(6));
  }

  /// <summary>
  /// The last completed week for a user whose local today is <paramref name="today"/>: the week before today's week.
  /// LazyTax took the week holding yesterday, which is only a completed week when run on a Sunday.
  /// </summary>
  public static (DateOnly Start, DateOnly End) Previous(DateOnly today) => SundayBounds(SundayBounds(today).Start.AddDays(-1));

  /// <summary>Only a day settled <c>done</c> counts as a success; frozen, vacation, skipped and missed do not (LazyTax: Succeeded).</summary>
  public static bool IsDone(string status) => string.Equals(status, "done", StringComparison.OrdinalIgnoreCase);

  /// <summary>A habit's week is perfect when it settled at least one day and every settled day is done.</summary>
  public static bool IsPerfect(IReadOnlyCollection<string> statuses) => statuses.Count > 0 && statuses.All(IsDone);
}
