using Ctx.Habit.Contract.V1;

namespace Ctx.Habit.Domain;

/// <summary>A habit's blueprint as the API sends it (strings), shared by the routes and the MCP tools.</summary>
public sealed record HabitInput(
  string Task,
  string[] DaysOfWeek,
  string NotificationTime,
  string Stake,
  Guid? CharityId,
  string Timezone,
  string? DeadlineTime);

/// <summary>A validated blueprint: money in cents, times parsed.</summary>
public sealed record VersionRecord(
  Guid? CharityId,
  string Task,
  string[] DaysOfWeek,
  TimeOnly NotificationTime,
  TimeOnly DeadlineTime,
  int StakeCents,
  string StakeCurrency,
  int RatioBasisPoints,
  string Timezone);

public sealed record HabitSearch(Guid? Id, string? Task, bool? Enabled, int Limit, int Skip);

public sealed record RoutineView(Guid Id, string UserId, string Name);

public sealed record RoutineSearch(string? Name, int Limit, int Skip);

/// <summary>Max stake in cents (LazyTax <c>HabitOption.MaxStakeCents</c>), from <c>Contexts:habit</c>.</summary>
public sealed record HabitSettings(int MaxStakeCents);

/// <summary>A schedulable habit before the account timezone is applied.</summary>
public sealed record SchedulableRow(Guid HabitId, string UserId, Guid VersionId, string[] DaysOfWeek, TimeOnly DeadlineTime, string HabitTimezone);
