using Ctx.Payment.Contract.V1;
using Ctx.Payment.Data;
using Ctx.Payment.Domain;
using LazyTax.Platform.Problems;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ctx.Payment.Tests.Unit;

public sealed class WebhookTests
{
  private static readonly PaymentSettings Settings = new() { WebhookSecret = "s3cret", WebhookToleranceSeconds = 300 };

  private static WebhookService Service(Rig rig, MemCustomers? customers = null) => new(Settings,
    new PaymentService(customers ?? rig.Customers, rig.Gateway, new FakeIdentityClaims(), new NullNotifier(), new InlineUow(),
      TimeProvider.System, NullLogger<PaymentService>.Instance),
    rig.Service(), TimeProvider.System, NullLogger<WebhookService>.Instance);

  private static string Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

  [Fact]
  public void A_correct_signature_passes()
  {
    var ts = Now();
    var body = """{"name":"x"}""";
    var act = () => Service(new Rig()).Verify(ts, WebhookService.Sign("s3cret", ts, body), body);
    act.Should().NotThrow();
  }

  [Fact]
  public void A_wrong_signature_is_401()
  {
    var ts = Now();
    var act = () => Service(new Rig()).Verify(ts, WebhookService.Sign("other", ts, "{}"), "{}");
    act.Should().Throw<Unauthenticated>().WithMessage("Incorrect Signature");
  }

  [Fact]
  public void A_tampered_body_is_401()
  {
    var ts = Now();
    var act = () => Service(new Rig()).Verify(ts, WebhookService.Sign("s3cret", ts, "{\"a\":1}"), "{\"a\":2}");
    act.Should().Throw<Unauthenticated>();
  }

  [Fact]
  public void A_stale_timestamp_is_401_even_when_signed()
  {
    var ts = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds().ToString();
    var act = () => Service(new Rig()).Verify(ts, WebhookService.Sign("s3cret", ts, "{}"), "{}");
    act.Should().Throw<Unauthenticated>().WithMessage("Stale*");
  }

  [Theory]
  [InlineData(null, null)]
  [InlineData("abc", "00")]
  public void Missing_or_malformed_headers_are_401(string? ts, string? sig)
  {
    var act = () => Service(new Rig()).Verify(ts, sig, "{}");
    act.Should().Throw<Unauthenticated>();
  }

  [Theory]
  [InlineData("scheduled", ConsentPurpose.Subscription)]
  [InlineData("SCHEDULED", ConsentPurpose.Subscription)]
  [InlineData("unscheduled", ConsentPurpose.Penalty)]
  [InlineData("", ConsentPurpose.Penalty)]
  [InlineData(null, ConsentPurpose.Penalty)]
  public void Trigger_reason_classifies_the_purpose(string? reason, ConsentPurpose expected) =>
    Purposes.FromTriggerReason(reason).Should().Be(expected);

  [Fact]
  public async Task A_verified_consent_event_stores_the_consent_for_its_purpose()
  {
    var rig = new Rig();
    await rig.Customers.Create("u1", "cus_1", CancellationToken.None);

    await Service(rig).Handle(new WebhookEvent("payment_consent.verified", "cst_9", "VERIFIED", "cus_1", "scheduled"), CancellationToken.None);

    var c = rig.Customers.ByUser["u1"];
    c.HasConsentFor(ConsentPurpose.Subscription).Should().BeTrue();
    c.HasConsentFor(ConsentPurpose.Penalty).Should().BeFalse();
  }

  private sealed class NullNotifier : IConsentNotifier
  {
    public Task ConsentChanged(string userId, bool linked, ConsentPurpose purpose, DateTime atUtc, CancellationToken ct) => Task.CompletedTask;
  }
}
