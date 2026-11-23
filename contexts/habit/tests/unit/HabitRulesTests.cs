#pragma warning disable xUnit1051 // tests run to completion; no cancellation token needed
using Ctx.Habit.Domain;
using LazyTax.Platform.Problems;

namespace Ctx.Habit.Tests.Unit;

public sealed class HabitRulesTests
{
  private static readonly HabitSettings Settings = new(1_000_000);

  private static HabitInput Ok() => new("Run", ["monday", "Tuesday", "monday"], "07:30:00", "10.50", Guid.NewGuid(), "Asia/Singapore", null);

  [Fact]
  public void Valid_input_is_canonicalised()
  {
    var r = HabitRules.Validate(Ok(), Settings, "x");
    r.DaysOfWeek.Should().Equal("Monday", "Tuesday");
    r.StakeCents.Should().Be(1050);
    r.RatioBasisPoints.Should().Be(10_000);
    r.DeadlineTime.Should().Be(new TimeOnly(23, 59));
    r.NotificationTime.Should().Be(new TimeOnly(7, 30));
  }

  [Fact]
  public void Zero_stake_needs_no_charity()
  {
    var r = HabitRules.Validate(Ok() with { Stake = "0", CharityId = null }, Settings, "x");
    r.StakeCents.Should().Be(0);
    r.CharityId.Should().BeNull();
  }

  [Theory]
  [InlineData("Task", "")]
  [InlineData("Stake", "-1")]
  [InlineData("Stake", "1.005")]
  [InlineData("Stake", "abc")]
  [InlineData("Stake", "10000.01")]
  [InlineData("Timezone", "Mars/Olympus")]
  [InlineData("NotificationTime", "7:30")]
  [InlineData("DeadlineTime", "25:00:00")]
  public void Bad_field_is_a_validation_error(string field, string value)
  {
    var input = field switch
    {
      "Task" => Ok() with { Task = value },
      "Stake" => Ok() with { Stake = value },
      "Timezone" => Ok() with { Timezone = value },
      "NotificationTime" => Ok() with { NotificationTime = value },
      _ => Ok() with { DeadlineTime = value },
    };
    var act = () => HabitRules.Validate(input, Settings, "x");
    act.Should().Throw<ValidationFailed>().Which.Status.Should().Be(400);
  }

  [Fact]
  public void Stake_without_charity_is_refused()
  {
    var act = () => HabitRules.Validate(Ok() with { CharityId = null }, Settings, "x");
    act.Should().Throw<ValidationFailed>().Which.Data.Should().BeEquivalentTo(new { errors = new Dictionary<string, string[]> { ["CharityId"] = ["A habit with a stake needs a charity."] } });
  }

  [Theory]
  [InlineData("1")]
  [InlineData("Monday,Tuesday")]
  [InlineData("")]
  public void Day_names_only(string day)
  {
    var act = () => HabitRules.Validate(Ok() with { DaysOfWeek = [day] }, Settings, "x");
    act.Should().Throw<ValidationFailed>();
  }

  [Fact]
  public void Page_limits()
  {
    HabitRules.Page(null, null).Should().Be((20, 0));
    ((Action)(() => HabitRules.Page(101, null))).Should().Throw<ValidationFailed>();
    ((Action)(() => HabitRules.Page(1, -1))).Should().Throw<ValidationFailed>();
  }
}
