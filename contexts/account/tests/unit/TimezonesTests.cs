using Ctx.Account.Domain;

namespace Ctx.Account.Tests.Unit;

public sealed class TimezonesTests
{
  [Theory]
  [InlineData("UTC", true)]
  [InlineData("Asia/Singapore", true)]
  [InlineData("Not/AZone", false)]
  [InlineData("", false)]
  public void IsValid(string tz, bool expected) => Timezones.IsValid(tz).Should().Be(expected);

  [Fact]
  public void Today_is_the_user_local_date()
  {
    var nowUtc = new DateTime(2026, 11, 22, 20, 0, 0, DateTimeKind.Utc); // 04:00 on the 23rd in Singapore
    Timezones.Today("Asia/Singapore", nowUtc).Should().Be(new DateOnly(2026, 11, 23));
    Timezones.Today("UTC", nowUtc).Should().Be(new DateOnly(2026, 11, 22));
  }
}
