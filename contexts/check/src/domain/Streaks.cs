using Ctx.Check.Contract.V1;

namespace Ctx.Check.Domain;

public enum DayStatus
{
  NotApplicable,
  Succeeded,
  Failed,
  Vacation,
  Skip,
  Frozen,
}

/// <summary>A habit's streak and week as the overview shows it (LazyTax HabitStreakStatus).</summary>
public sealed record StreakStatus(
  int CurrentStreak,
  int MaxStreak,
  bool IsCompleteToday,
  IReadOnlyDictionary<DayOfWeek, DayStatus> WeekStatuses,
  DateOnly WeekStart,
  DateOnly WeekEnd,
  int TimeLeftToEodMinutes,
  DateOnly Today,
  DayStatus TodayStatus);

/// <summary>Streak math (LazyTax StreakService / StreakCalculator / GetUserMaxStreakAcrossHabits), pure.</summary>
public static class Streaks
{
  /// <summary>Current: Done days after the last Missed day, up to today.</summary>
  public static int Current(IReadOnlyList<ExecutionDay> executions, DateOnly today)
  {
    var since = executions.Where(e => e.Date <= today && e.Status == CheckStatus.Missed)
      .Select(e => (DateOnly?)e.Date).Max() ?? DateOnly.MinValue;
    return executions.Count(e => e.Status == CheckStatus.Done && e.Date > since && e.Date <= today);
  }

  /// <summary>Max: the longest run of Done days, oldest first; a Missed day resets it, other statuses don't.</summary>
  public static int Max(IEnumerable<ExecutionDay> executionsOldestFirst)
  {
    int run = 0, max = 0;
    foreach (var e in executionsOldestFirst)
    {
      if (e.Status == CheckStatus.Done) max = Math.Max(max, ++run);
      else if (e.Status == CheckStatus.Missed) run = 0;
    }
    return max;
  }

  public static (DateOnly Start, DateOnly End) WeekSundayBounds(DateOnly today)
  {
    var daysSinceSunday = ((int)today.DayOfWeek + 7 - (int)DayOfWeek.Sunday) % 7;
    var start = today.AddDays(-daysSinceSunday);
    return (start, start.AddDays(6));
  }

  /// <summary>
  /// One timezone (the user's) drives today, the week and the time left. Each day's status comes from its
  /// execution's own date, so past days never move when the user travels.
  /// </summary>
  public static StreakStatus Status(IReadOnlyList<ExecutionDay> executions, string timezone, TimeOnly deadlineTime, DateTime utcNow)
  {
    var tz = CheckWindow.Resolve(timezone);
    var today = CheckWindow.LocalDate(utcNow, tz);
    var (weekStart, weekEnd) = WeekSundayBounds(today);
    var ordered = executions.OrderBy(e => e.Date).ToList();

    var week = Enum.GetValues<DayOfWeek>().ToDictionary(d => d, _ => DayStatus.NotApplicable);
    var inWeek = ordered.Where(e => e.Date >= weekStart && e.Date <= weekEnd).ToList();
    foreach (var e in inWeek) week[e.Date.DayOfWeek] = Map(e.Status);

    var todayExecution = inWeek.FirstOrDefault(e => e.Date == today);
    var (_, deadlineUtc) = CheckWindow.For(today, tz, deadlineTime);
    var minutesLeft = (int)Math.Max(0, Math.Round((deadlineUtc - utcNow).TotalMinutes));

    return new StreakStatus(
      Current(ordered, today),
      Max(ordered),
      todayExecution?.Status == CheckStatus.Done,
      week,
      weekStart,
      weekEnd,
      minutesLeft,
      today,
      todayExecution is null ? DayStatus.NotApplicable : Map(todayExecution.Status));
  }

  public static DayStatus Map(CheckStatus s) => s switch
  {
    CheckStatus.Done => DayStatus.Succeeded,
    CheckStatus.Missed => DayStatus.Failed,
    CheckStatus.Skipped => DayStatus.Skip,
    CheckStatus.Frozen => DayStatus.Frozen,
    CheckStatus.Vacation => DayStatus.Vacation,
    _ => DayStatus.NotApplicable,
  };
}
