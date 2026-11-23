using Ctx.Check.Contract.V1;

namespace Ctx.Check.Domain;

/// <summary>The new window of a Pending check after a habit edit or a timezone move (pure; LazyTax CheckService.Retime).</summary>
public static class Retime
{
  /// <summary>What to do with one Pending check.</summary>
  public abstract record Step;

  /// <summary>The habit is gone, disabled or no longer scheduled on that weekday: delete the check.</summary>
  public sealed record Remove : Step;

  public sealed record Keep : Step;

  public sealed record Move(string Timezone, DateTime OpensAtUtc, DateTime DeadlineUtc) : Step;

  /// <summary>
  /// <paramref name="followTimezone"/>: a timezone change, where the window follows the new timezone either
  /// way. Otherwise (a habit edit) a started check never gets a shorter deadline, and a check that hasn't
  /// started takes the new window unless that window is already over.
  /// </summary>
  public static Step For(DailyCheck c, SchedulableHabit? h, DateTime now, bool followTimezone)
  {
    if (h is null || !h.IsScheduledOn(c.LocalDate)) return new Remove();

    var tz = CheckWindow.Resolve(h.Timezone);
    var (opens, deadline) = CheckWindow.For(c.LocalDate, tz, h.DeadlineTime);
    var started = c.OpensAtUtc <= now;
    var newDeadline = followTimezone
      ? deadline
      : started
        ? (deadline > c.DeadlineUtc ? deadline : c.DeadlineUtc)
        : (deadline > now ? deadline : c.DeadlineUtc);
    if (opens >= newDeadline) opens = c.OpensAtUtc;

    if (tz.Id == c.Timezone && opens == c.OpensAtUtc && newDeadline == c.DeadlineUtc) return new Keep();
    return new Move(tz.Id, opens, newDeadline);
  }

  /// <summary>The checks the tick opens for a habit: today and tomorrow (user-local), scheduled, deadline not passed.</summary>
  public static IEnumerable<NewCheck> Upcoming(SchedulableHabit h, DateTime nowUtc)
  {
    var tz = CheckWindow.Resolve(h.Timezone);
    var today = CheckWindow.LocalDate(nowUtc, tz);
    foreach (var date in new[] { today, today.AddDays(1) })
    {
      if (!h.IsScheduledOn(date)) continue;
      var (opens, deadline) = CheckWindow.For(date, tz, h.DeadlineTime);
      // Never open a day whose deadline already passed (e.g. a habit created after its deadline).
      if (deadline <= nowUtc) continue;
      yield return new NewCheck(h.HabitId, h.UserId, date, tz.Id, opens, deadline);
    }
  }
}
