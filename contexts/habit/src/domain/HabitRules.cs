using System.Globalization;
using LazyTax.Platform.Problems;

namespace Ctx.Habit.Domain;

/// <summary>Request rules shared by the routes and the MCP tools (LazyTax <c>HabitValidator.cs</c>).</summary>
public static class HabitRules
{
  public const string TimeFormat = "HH:mm:ss";
  public static readonly TimeOnly DefaultDeadline = new(23, 59);

  // A weekday name in any case ("Friday", "friday"). Numbers ("1") and lists ("Monday,Tuesday") are not names,
  // although Enum.TryParse accepts them.
  public static bool TryParseDay(string? day, out DayOfWeek value)
  {
    value = default;
    return !string.IsNullOrEmpty(day) && day.All(char.IsLetter) && Enum.TryParse(day, true, out value);
  }

  public static string CanonicalDay(string day) => TryParseDay(day, out var d) ? d.ToString() : day;

  public static bool TryParseStake(string? s, out decimal d) =>
    decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out d);

  public static bool HasMoneyStake(string? stake) => TryParseStake(stake, out var d) && d > 0;

  public static bool TryTime(string? s, out TimeOnly t) =>
    TimeOnly.TryParseExact(s, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out t);

  /// <summary>Validates the input and returns the blueprint; throws <see cref="ValidationFailed"/> with every field error.</summary>
  public static VersionRecord Validate(HabitInput req, HabitSettings settings, string what)
  {
    var errors = new Dictionary<string, List<string>>();
    void Add(string field, string msg)
    {
      if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
      list.Add(msg);
    }

    if (string.IsNullOrWhiteSpace(req.Task)) Add("Task", "'Task' must not be empty.");
    else if (req.Task.Length > 256) Add("Task", "The length of 'Task' must be 256 characters or fewer.");

    if (req.DaysOfWeek is null || req.DaysOfWeek.Length == 0) Add("DaysOfWeek", "'Days Of Week' must not be empty.");
    else if (!req.DaysOfWeek.All(d => TryParseDay(d, out _))) Add("DaysOfWeek", "DaysOfWeek must contain valid day names (e.g., Monday, Tuesday).");

    TimeOnly notification = default;
    if (!TryTime(req.NotificationTime, out notification)) Add("NotificationTime", $"TimeOnly must be in the format of {TimeFormat}");

    var stakeCents = 0;
    if (string.IsNullOrWhiteSpace(req.Stake)) Add("Stake", "'Stake' must not be empty.");
    else if (!TryParseStake(req.Stake, out var stake)) Add("Stake", "Stake must be a valid decimal number.");
    else if (stake < 0) Add("Stake", "Stake must be non-negative.");
    else if (decimal.Round(stake, 2) != stake) Add("Stake", "Stake can have at most 2 decimal places.");
    else if (stake > settings.MaxStakeCents / 100m) Add("Stake", $"Stake must be at most {settings.MaxStakeCents / 100m:F2}.");
    else stakeCents = (int)(stake * 100);

    if (HasMoneyStake(req.Stake) && (req.CharityId is null || req.CharityId == Guid.Empty))
      Add("CharityId", "A habit with a stake needs a charity.");

    if (string.IsNullOrWhiteSpace(req.Timezone)) Add("Timezone", "'Timezone' must not be empty.");
    else if (!TimeZoneInfo.TryFindSystemTimeZoneById(req.Timezone, out _))
      Add("Timezone", "Timezone must be a valid timezone identifier (e.g. 'Asia/Singapore', 'America/New_York')");

    var deadline = DefaultDeadline;
    if (req.DeadlineTime is not null && !TryTime(req.DeadlineTime, out deadline))
      Add("DeadlineTime", $"TimeOnly must be in the format of {TimeFormat}");

    if (errors.Count > 0)
      throw new ValidationFailed(what, errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));

    return new VersionRecord(
      req.CharityId == Guid.Empty ? null : req.CharityId,
      req.Task,
      req.DaysOfWeek!.Select(CanonicalDay).Distinct().ToArray(),
      notification,
      deadline,
      stakeCents,
      "USD",
      10_000, // fixed at 100%: all the stake goes to charity
      req.Timezone);
  }

  /// <summary>Limit 0..100 and Skip >= 0 (LazyTax <c>ValidationUtility.Limit / Skip</c>), with LazyTax's defaults.</summary>
  public static (int Limit, int Skip) Page(int? limit, int? skip)
  {
    var errors = new Dictionary<string, string[]>();
    if (limit is < 0 or > 100) errors["Limit"] = ["Limit has to be between 0 to 100 characters"];
    if (skip is < 0) errors["Skip"] = ["Skip has to be larger than or equal to 0"];
    if (errors.Count > 0) throw new ValidationFailed("Invalid query", errors);
    return (limit ?? 20, skip ?? 0);
  }
}
