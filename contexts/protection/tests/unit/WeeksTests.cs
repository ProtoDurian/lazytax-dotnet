using Ctx.Protection.Domain;

namespace Ctx.Protection.Tests.Unit;

public sealed class WeeksTests
{
  [Fact]
  public void Week_runs_sunday_to_saturday()
  {
    // 2026-11-18 is a Wednesday.
    var (s, e) = Weeks.SundayBounds(new DateOnly(2026, 11, 18));
    s.Should().Be(new DateOnly(2026, 11, 15));
    e.Should().Be(new DateOnly(2026, 11, 21));
    s.DayOfWeek.Should().Be(DayOfWeek.Sunday);
  }

  [Fact]
  public void A_sunday_starts_its_own_week()
  {
    var (s, _) = Weeks.SundayBounds(new DateOnly(2026, 11, 22));
    s.Should().Be(new DateOnly(2026, 11, 22));
  }

  [Fact]
  public void On_a_monday_the_last_completed_week_is_the_one_that_ended_yesterday()
  {
    // Monday Nov 23: the week Nov 22..28 has just begun, so the last completed week is Nov 15..21.
    var (s, e) = Weeks.Previous(new DateOnly(2026, 11, 23));
    s.Should().Be(new DateOnly(2026, 11, 15));
    e.Should().Be(new DateOnly(2026, 11, 21));
  }

  [Fact]
  public void On_a_sunday_the_last_completed_week_is_the_one_ending_yesterday()
  {
    // LazyTax's formula (the week holding yesterday) gives the same on a Sunday.
    var (s, e) = Weeks.Previous(new DateOnly(2026, 11, 22));
    s.Should().Be(new DateOnly(2026, 11, 15));
    e.Should().Be(new DateOnly(2026, 11, 21));
  }

  [Fact]
  public void On_a_saturday_the_last_completed_week_is_the_one_before_this_week()
  {
    // Saturday Nov 21 is still inside Nov 15..21, so the last completed week is Nov 8..14.
    var (s, e) = Weeks.Previous(new DateOnly(2026, 11, 21));
    s.Should().Be(new DateOnly(2026, 11, 8));
    e.Should().Be(new DateOnly(2026, 11, 14));
  }

  [Theory]
  [InlineData(true, "done", "done")]
  [InlineData(true, "Done")]
  [InlineData(false, "done", "missed")]
  [InlineData(false, "done", "frozen")]
  [InlineData(false, "done", "vacation")]
  [InlineData(false, "skipped")]
  [InlineData(false)] // no settled day: not vacuously perfect
  public void Perfect_needs_a_day_and_every_day_done(bool expected, params string[] statuses) =>
    Weeks.IsPerfect(statuses).Should().Be(expected);
}
