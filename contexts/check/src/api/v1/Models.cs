using System.Globalization;
using Ctx.Check.Contract.V1;
using Ctx.Check.Domain;
using LazyTax.Platform.Problems;

namespace Ctx.Check.Api.V1;

public sealed record CompleteHabitReq(string? Notes);

public sealed record SkipHabitReq(string? Notes);

public sealed record SearchHabitExecutionQuery(Guid? Id, string? Date, int? Limit, int? Skip);

public sealed record OverviewQuery(int? Limit, int? Skip);

/// <summary>LazyTax HabitExecutionRes. Status is one of succeeded, failed, vacation, skip, frozen.</summary>
public sealed record HabitExecutionRes(Guid Id, Guid HabitVersionId, string Date, string Status, string? CompletedAt, string? Notes, bool PaymentProcessed);

public sealed record MarkDailyFailuresCronRes(int TotalMarked);

public sealed record HabitOverviewResponse(List<HabitOverviewHabitRes> Habits, string TotalDebt, int UsedSkip, int TotalSkip);

public sealed record HabitOverviewHabitRes(
  string Id,
  string Name,
  string NotificationTime,
  string Timezone,
  bool[] Days,
  StakeRes Stake,
  bool Enabled,
  HabitCharityRefRes? Charity,
  HabitStatusRes Status,
  int TimeLeftToEodMinutes,
  HabitVersionMetaRes Version,
  string TotalDebt,
  string DeadlineTime,
  DateTime? TodayDeadlineUtc,
  string Proof,
  string? RoutineId,
  int SortOrder);

public sealed record StakeRes(decimal Amount, string Currency);

public sealed record HabitCharityRefRes(string Id, string Name, string? Url);

public sealed record HabitStatusRes(int CurrentStreak, int MaxStreak, bool IsCompleteToday, WeekStatusRes Week, string Today, string TodayStatus);

public sealed record WeekStatusRes(
  string Sunday,
  string Monday,
  string Tuesday,
  string Wednesday,
  string Thursday,
  string Friday,
  string Saturday,
  string Start,
  string End);

public sealed record HabitVersionMetaRes(string Id, ushort Version, bool IsActive);

public static class Mapper
{
  public const string DateFormat = "dd-MM-yyyy";

  public static HabitExecutionRes ToRes(this HabitExecution e) => new(
    e.Id,
    e.HabitVersionId,
    e.Date.ToString(DateFormat, CultureInfo.InvariantCulture),
    e.Status switch
    {
      CheckStatus.Done => "succeeded",
      CheckStatus.Skipped => "skip",
      CheckStatus.Frozen => "frozen",
      CheckStatus.Vacation => "vacation",
      _ => "failed",
    },
    e.CompletedAtUtc?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
    e.Notes,
    false);

  /// <summary>LazyTax's Limit (0..100) and Skip (&gt;= 0) rules.</summary>
  public static (int Limit, int Skip) Page(int? limit, int? skip, string what)
  {
    if (limit is < 0 or > 100) throw new ValidationFailed($"Invalid {what}", "limit", "Limit has to be between 0 to 100 characters");
    if (skip is < 0) throw new ValidationFailed($"Invalid {what}", "skip", "Skip has to be larger than or equal to 0");
    return (limit ?? 20, skip ?? 0);
  }

  public static ExecutionSearch ToSearch(this SearchHabitExecutionQuery q)
  {
    var (limit, skip) = Page(q.Limit, q.Skip, "SearchHabitExecutionQuery");
    DateOnly? date = null;
    if (q.Date is not null)
    {
      if (!DateOnly.TryParseExact(q.Date, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        throw new ValidationFailed("Invalid SearchHabitExecutionQuery", "date", $"DateOnly must be in the format of {DateFormat}");
      date = d;
    }
    return new ExecutionSearch(q.Id, date, limit, skip);
  }

  public static HabitOverviewResponse ToRes(this OverviewSummary s) => new(
    [.. s.Items.Select(i => i.ToRes(s.Timezone))], Money(s.TotalDebtCents), s.UsedSkip, s.TotalSkip);

  private static HabitOverviewHabitRes ToRes(this OverviewItem i, string tz)
  {
    var v = i.Version;
    return new HabitOverviewHabitRes(
      v.HabitId.ToString(),
      v.Task,
      v.NotificationTime.ToString("HH:mm", CultureInfo.InvariantCulture),
      tz,
      Days(v.DaysOfWeek),
      new StakeRes(v.StakeCents / 100m, v.StakeCurrency),
      v.Enabled,
      i.HasCharity ? new HabitCharityRefRes(v.CharityId!.Value.ToString(), i.Charity?.Name ?? string.Empty, null) : null,
      Status(i.Status),
      i.Status.TimeLeftToEodMinutes,
      new HabitVersionMetaRes(v.Id.ToString(), v.Version, true),
      Money(i.DebtCents),
      v.DeadlineTime.ToString("HH:mm", CultureInfo.InvariantCulture),
      i.TodayDeadlineUtc is { } d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null,
      i.Proof,
      v.RoutineId?.ToString(),
      v.SortOrder);
  }

  private static string Money(long cents) => (cents / 100m).ToString("F2", CultureInfo.InvariantCulture);

  private static bool[] Days(string[] days)
  {
    var set = days.Select(x => x.ToLowerInvariant()).ToHashSet();
    return [.. new[] { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" }.Select(set.Contains)];
  }

  private static HabitStatusRes Status(StreakStatus s) => new(
    s.CurrentStreak,
    s.MaxStreak,
    s.IsCompleteToday,
    new WeekStatusRes(
      Day(s.WeekStatuses[DayOfWeek.Sunday]),
      Day(s.WeekStatuses[DayOfWeek.Monday]),
      Day(s.WeekStatuses[DayOfWeek.Tuesday]),
      Day(s.WeekStatuses[DayOfWeek.Wednesday]),
      Day(s.WeekStatuses[DayOfWeek.Thursday]),
      Day(s.WeekStatuses[DayOfWeek.Friday]),
      Day(s.WeekStatuses[DayOfWeek.Saturday]),
      s.WeekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
      s.WeekEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
    s.Today.ToString(DateFormat, CultureInfo.InvariantCulture),
    Day(s.TodayStatus));

  private static string Day(DayStatus s) => s switch
  {
    DayStatus.Succeeded => "succeeded",
    DayStatus.Failed => "failed",
    DayStatus.Vacation => "vacation",
    DayStatus.Skip => "skip",
    DayStatus.Frozen => "frozen",
    _ => "not_applicable",
  };
}
