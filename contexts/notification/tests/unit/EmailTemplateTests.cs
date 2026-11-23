using Ctx.Notification.Contract.V1;
using Ctx.Notification.Domain;

namespace Ctx.Notification.Tests.Unit;

public sealed class EmailTemplateTests
{
  private static readonly Dictionary<string, string> Vars = new()
  {
    ["BaseUrl"] = "https://lazytax.club",
    ["UserName"] = "Sarah",
    ["SupportEmail"] = "support@lazytax.club",
    ["Amount"] = "$5.00",
    ["CharityName"] = "Doctors Without Borders",
    ["HabitName"] = "Morning run",
    ["ChargeDate"] = "12 July 2026",
    ["AttemptDate"] = "12 July 2026",
    ["Tier"] = "Pro",
    ["NextBillingDate"] = "12 August 2026",
    ["GraceEndsDate"] = "19 July 2026",
    ["ChangeSummary"] = "Pro → Free",
    ["EffectiveDate"] = "12 August 2026",
    ["Purpose"] = "Habit stakes",
    ["ChangeDate"] = "12 July 2026",
    ["MemberSince"] = "January 2026",
    ["MembershipType"] = "Pro",
    ["Code"] = "482619",
  };

  public static TheoryData<string> Ids() => [.. EmailTemplateIds.All];

  [Theory]
  [MemberData(nameof(Ids))]
  public void Every_template_renders_with_no_residue(string id)
  {
    var mail = EmailTemplates.Render(id, Vars);
    mail.Html.Should().NotContain("{{").And.Contain("Sarah").And.Contain("support@lazytax.club");
    mail.Subject.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public void A_missing_variable_fails_the_render()
  {
    var vars = new Dictionary<string, string>(Vars);
    vars.Remove("Amount");
    var act = () => EmailTemplates.Render(EmailTemplateIds.PenaltyCharged, vars);
    act.Should().Throw<MissingVariable>().WithMessage("*Amount*");
  }

  [Fact]
  public void Variables_are_html_escaped()
  {
    var vars = new Dictionary<string, string>(Vars) { ["UserName"] = "<script>x</script>" };
    EmailTemplates.Render(EmailTemplateIds.Welcome, vars).Html.Should().NotContain("<script>");
  }

  [Fact]
  public void Subjects_match_LazyTax() =>
    EmailTemplates.Render(EmailTemplateIds.PenaltyCharged, Vars).Subject.Should().Be("Your LazyTax habit stake receipt");

  [Fact]
  public void Unknown_template_is_refused() =>
    ((Action)(() => EmailTemplates.Render("nope", Vars))).Should().Throw<ArgumentException>();

  [Theory]
  [InlineData(500, "USD", "$5.00")]
  [InlineData(1999, "EUR", "19.99 EUR")]
  public void Money_is_formatted_as_in_LazyTax(long cents, string currency, string expected) =>
    PenaltyNotifier.FormatMoney(cents, currency).Should().Be(expected);

  [Fact]
  public void Date_is_formatted_and_falls_back()
  {
    var fallback = new DateTime(2026, 7, 12, 0, 0, 0, DateTimeKind.Utc);
    PenaltyNotifier.FormatDate("2026-01-05T10:00:00Z", fallback).Should().Be("5 January 2026");
    PenaltyNotifier.FormatDate(null, fallback).Should().Be("12 July 2026");
    PenaltyNotifier.FormatDate("garbage", fallback).Should().Be("12 July 2026");
  }
}
