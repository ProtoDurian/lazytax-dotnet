using Ctx.Vacation.Api.V1;
using LazyTax.Platform.Problems;

namespace Ctx.Vacation.Tests.Unit;

public sealed class MapperTests
{
  [Fact]
  public void Parses_LazyTax_dates()
  {
    var r = new CreateVacationReq("01-02-2027", "03-02-2027", "UTC").ToRecord();
    r.StartDate.Should().Be(new DateOnly(2027, 2, 1));
    r.EndDate.Should().Be(new DateOnly(2027, 2, 3));
  }

  [Fact]
  public void Bad_dates_and_timezone_are_one_400() =>
    FluentActions.Invoking(() => new CreateVacationReq("2027-02-01", "x", "Nope/Zone").ToRecord())
      .Should().Throw<ValidationFailed>();
}
