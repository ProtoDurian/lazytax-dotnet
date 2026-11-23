using System.Globalization;
using Ctx.Habit.Contract.V1;
using Ctx.Habit.Domain;
using LazyTax.Platform.Problems;

namespace Ctx.Habit.Api.V1;

public sealed record CreateHabitReq(
  string Task,
  string[] DaysOfWeek,
  string NotificationTime, // "HH:mm:ss"
  string Stake,            // decimal string, e.g. "10.50"
  Guid? CharityId,         // needed only when Stake > 0
  string Timezone,         // e.g. "Asia/Singapore"
  string? DeadlineTime = null, // "HH:mm:ss"; defaults to 23:59:00
  Guid? RoutineId = null); // existing routine of the user; omitted => new one-habit routine

public sealed record UpdateHabitReq(
  string Task,
  string[] DaysOfWeek,
  string NotificationTime,
  string Stake,
  Guid? CharityId,
  bool Enabled,
  string Timezone,
  string? DeadlineTime = null);

public sealed record ReorderHabitsReq(string[] HabitIds);

public sealed record ReorderHabitsRes(string[] HabitIds);

public sealed record HabitVersionRes(
  Guid Id,
  Guid HabitId,
  ushort Version,
  string Task,
  string[] DaysOfWeek,
  string NotificationTime,
  string Stake,
  string Ratio,
  Guid? CharityId,
  string Timezone,
  string DeadlineTime);

public sealed record SearchHabitQuery(Guid? Id, string? UserId, string? Task, bool? Enabled, int? Limit, int? Skip);

public sealed record CreateRoutineReq(string Name);

public sealed record UpdateRoutineReq(string Name);

public sealed record RoutineRes(Guid Id, string UserId, string Name);

public sealed record SearchRoutineQuery(string? Name, int? Limit, int? Skip);

public static class HabitMapper
{
  public static HabitInput ToInput(this CreateHabitReq r) =>
    new(r.Task, r.DaysOfWeek, r.NotificationTime, r.Stake, r.CharityId, r.Timezone, r.DeadlineTime);

  public static HabitInput ToInput(this UpdateHabitReq r) =>
    new(r.Task, r.DaysOfWeek, r.NotificationTime, r.Stake, r.CharityId, r.Timezone, r.DeadlineTime);

  public static HabitVersionRes ToRes(this HabitVersionView v) => new(
    v.Id,
    v.HabitId,
    v.Version,
    v.Task,
    v.DaysOfWeek,
    v.NotificationTime.ToString(HabitRules.TimeFormat, CultureInfo.InvariantCulture),
    (v.StakeCents / 100m).ToString("F2", CultureInfo.InvariantCulture),
    (v.RatioBasisPoints / 100m).ToString("F1", CultureInfo.InvariantCulture),
    v.CharityId,
    v.Timezone,
    v.DeadlineTime.ToString(HabitRules.TimeFormat, CultureInfo.InvariantCulture));

  public static RoutineRes ToRes(this RoutineView r) => new(r.Id, r.UserId, r.Name);

  public static Guid[] ParseIds(string[]? ids)
  {
    if (ids is null) throw new ValidationFailed("Invalid ReorderHabitsReq", "HabitIds", "'Habit Ids' must not be empty.");
    return ids.Select(s => Guid.TryParse(s, out var g)
      ? g
      : throw new ValidationFailed("Invalid ReorderHabitsReq", "HabitIds", "Each habit id must be a GUID.")).ToArray();
  }
}
