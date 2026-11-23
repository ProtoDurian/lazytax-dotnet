using Ctx.Check.Contract.V1;
using Ctx.Check.Domain;

namespace Ctx.Check.Tests.Unit;

public sealed class CheckWindowTests
{
  [Fact]
  public void Deadline_2359_means_until_local_midnight()
  {
    var tz = CheckWindow.Resolve("Asia/Singapore");
    var (opens, due) = CheckWindow.For(new DateOnly(2026, 3, 1), tz, new TimeOnly(23, 59));
    opens.Should().Be(new DateTime(2026, 2, 28, 16, 0, 0, DateTimeKind.Utc));
    due.Should().Be(new DateTime(2026, 3, 1, 16, 0, 0, DateTimeKind.Utc));
  }

  [Fact]
  public void Unknown_timezone_falls_back_to_utc() => CheckWindow.Resolve("Mars/Olympus").Should().Be(TimeZoneInfo.Utc);

  [Fact]
  public void A_window_in_a_dst_gap_moves_forward()
  {
    // New York skips 02:00-03:00 on 8 Mar 2026; a 02:30 deadline lands after the gap.
    var tz = CheckWindow.Resolve("America/New_York");
    var (opens, due) = CheckWindow.For(new DateOnly(2026, 3, 8), tz, new TimeOnly(2, 30));
    due.Should().BeAfter(opens);
    due.Should().Be(new DateTime(2026, 3, 8, 7, 1, 0, DateTimeKind.Utc));
  }

  [Fact]
  public void Month_of_a_day() =>
    CheckWindow.MonthOf(new DateOnly(2024, 2, 14)).Should().Be((new DateOnly(2024, 2, 1), new DateOnly(2024, 2, 29)));
}

public sealed class StreakTests
{
  private static ExecutionDay D(int day, CheckStatus s) => new(new DateOnly(2026, 1, day), s, null);

  [Fact]
  public void Missed_resets_and_other_statuses_do_not()
  {
    var days = new[]
    {
      D(1, CheckStatus.Done), D(2, CheckStatus.Done), D(3, CheckStatus.Missed),
      D(4, CheckStatus.Done), D(5, CheckStatus.Skipped), D(6, CheckStatus.Frozen), D(7, CheckStatus.Done),
    };
    Streaks.Max(days).Should().Be(2);
    Streaks.Current(days, new DateOnly(2026, 1, 7)).Should().Be(2);
    Streaks.Current(days, new DateOnly(2026, 1, 2)).Should().Be(2);
  }

  [Fact]
  public void Week_status_runs_sunday_to_saturday()
  {
    // Wed 7 Jan 2026, noon UTC.
    var now = new DateTime(2026, 1, 7, 12, 0, 0, DateTimeKind.Utc);
    var s = Streaks.Status([D(4, CheckStatus.Done), D(5, CheckStatus.Vacation), D(7, CheckStatus.Done)], "UTC", new TimeOnly(23, 59), now);
    s.WeekStart.Should().Be(new DateOnly(2026, 1, 4));
    s.WeekEnd.Should().Be(new DateOnly(2026, 1, 10));
    s.WeekStatuses[DayOfWeek.Sunday].Should().Be(DayStatus.Succeeded);
    s.WeekStatuses[DayOfWeek.Monday].Should().Be(DayStatus.Vacation);
    s.WeekStatuses[DayOfWeek.Tuesday].Should().Be(DayStatus.NotApplicable);
    s.IsCompleteToday.Should().BeTrue();
    s.TodayStatus.Should().Be(DayStatus.Succeeded);
    s.TimeLeftToEodMinutes.Should().Be(720);
  }
}

public sealed class RetimeTests
{
  private static readonly DateOnly Day = new(2026, 1, 7); // a Wednesday

  private static SchedulableHabit Habit(string tz = "UTC", TimeOnly? deadline = null, string[]? days = null) =>
    new(Guid.NewGuid(), "u", Guid.NewGuid(), days ?? ["Wednesday"], deadline ?? new TimeOnly(23, 59), tz, 0, 0, "USD", null);

  private static DailyCheck Pending(SchedulableHabit h, DateTime opens, DateTime due) =>
    new(Guid.NewGuid(), h.HabitId, "u", Day, "UTC", opens, due, CheckStatus.Pending, null, null, null);

  [Fact]
  public void An_unscheduled_day_is_removed()
  {
    var h = Habit(days: ["Monday"]);
    Retime.For(Pending(h, default, default), h, DateTime.UtcNow, false).Should().BeOfType<Retime.Remove>();
    Retime.For(Pending(h, default, default), null, DateTime.UtcNow, false).Should().BeOfType<Retime.Remove>();
  }

  [Fact]
  public void A_started_check_never_gets_a_shorter_deadline_on_a_habit_edit()
  {
    var h = Habit(deadline: new TimeOnly(12, 0));
    var (opens, due) = CheckWindow.For(Day, TimeZoneInfo.Utc, new TimeOnly(23, 59));
    var now = Day.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
    Retime.For(Pending(h, opens, due), h, now, followTimezone: false).Should().BeOfType<Retime.Keep>();
  }

  [Fact]
  public void A_timezone_move_follows_the_new_zone()
  {
    var h = Habit(tz: "Asia/Singapore");
    var (opens, due) = CheckWindow.For(Day, TimeZoneInfo.Utc, new TimeOnly(23, 59));
    var now = Day.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
    var step = Retime.For(Pending(h, opens, due), h, now, followTimezone: true).Should().BeOfType<Retime.Move>().Subject;
    step.DeadlineUtc.Should().Be(new DateTime(2026, 1, 7, 16, 0, 0, DateTimeKind.Utc));
  }

  [Fact]
  public void Upcoming_skips_a_day_whose_deadline_passed()
  {
    var h = Habit(days: ["Wednesday", "Thursday"], deadline: new TimeOnly(8, 0));
    var now = Day.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
    Retime.Upcoming(h, now).Select(c => c.LocalDate).Should().Equal(Day.AddDays(1));
  }
}
