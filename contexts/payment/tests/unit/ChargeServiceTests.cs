using System.Transactions;
using Ctx.Payment.Contract.V1;
using Ctx.Payment.Domain;

namespace Ctx.Payment.Tests.Unit;

public sealed class ChargeServiceTests
{
  [Fact]
  public async Task First_charge_creates_persists_then_confirms_and_emits_completed()
  {
    var rig = new Rig();
    rig.Customers.AddVerified("u1");
    var id = Guid.NewGuid();

    var res = await rig.Charge(id);

    res.Status.Should().Be(ChargeStatus.Succeeded);
    res.Attempt.Should().Be(1);
    rig.Gateway.Calls.Should().ContainInOrder($"create:{id}-1", $"confirm:{res.IntentId}");
    rig.Charges.Rows[id].IntentId.Should().Be(res.IntentId);
    rig.Outbox.Events.Should().ContainSingle(e => e.Name == PaymentEvents.Completed)
      .Which.Payload.Should().BeOfType<PaymentSettled>().Which.Kind.Should().Be("penalty");
  }

  [Fact]
  public async Task Charging_a_succeeded_request_again_never_calls_the_gateway()
  {
    var rig = new Rig();
    rig.Customers.AddVerified("u1");
    var id = Guid.NewGuid();
    var first = await rig.Charge(id);
    var calls = rig.Gateway.Calls.Count;

    var again = await rig.Charge(id);

    again.Should().BeEquivalentTo(first);
    rig.Gateway.Calls.Count.Should().Be(calls);
    rig.Gateway.ChargesMoved("cus_u1").Should().Be(1);
  }

  [Fact]
  public async Task Confirm_failure_keeps_the_intent_and_the_retry_reconciles_it_without_a_new_create()
  {
    var rig = new Rig();
    rig.Customers.AddVerified("u1");
    var id = Guid.NewGuid();
    rig.Gateway.FailNext("confirm");

    var failed = await rig.Charge(id);
    failed.Status.Should().Be(ChargeStatus.Failed);
    failed.IntentId.Should().NotBeNull();
    rig.Outbox.Events.Should().Contain(e => e.Name == PaymentEvents.Failed);

    var retried = await rig.Charge(id);

    retried.Status.Should().Be(ChargeStatus.Succeeded);
    retried.IntentId.Should().Be(failed.IntentId);
    rig.Ops("create").Should().ContainSingle();
    rig.Gateway.Calls.Should().ContainInOrder($"retrieve:{failed.IntentId}", $"confirm:{failed.IntentId}");
  }

  [Fact]
  public async Task Ambiguous_create_failure_redrives_the_same_idempotency_key()
  {
    var rig = new Rig();
    rig.Customers.AddVerified("u1");
    var id = Guid.NewGuid();
    rig.Gateway.FailNext("create", definitive: false);

    var first = await rig.Charge(id);
    first.Status.Should().Be(ChargeStatus.Pending); // stays Processing: the intent may exist at the gateway
    rig.Charges.Rows[id].State.Should().Be(ChargeState.Processing);

    var second = await rig.Charge(id);

    second.Status.Should().Be(ChargeStatus.Succeeded);
    second.Attempt.Should().Be(1);
    rig.Ops("create").Should().AllBe($"create:{id}-1").And.HaveCount(2);
  }

  [Fact]
  public async Task Definitive_create_rejection_mints_the_next_attempt_key()
  {
    var rig = new Rig();
    rig.Customers.AddVerified("u1");
    var id = Guid.NewGuid();
    rig.Gateway.FailNext("create", definitive: true);

    (await rig.Charge(id)).Status.Should().Be(ChargeStatus.Failed);
    var second = await rig.Charge(id);

    second.Status.Should().Be(ChargeStatus.Succeeded);
    second.Attempt.Should().Be(2);
    rig.Ops("create").Should().Equal($"create:{id}-1", $"create:{id}-2");
  }

  [Fact]
  public async Task Unsettled_intent_is_pending_and_a_later_call_settles_it()
  {
    var rig = new Rig();
    rig.Customers.AddVerified("u1");
    var id = Guid.NewGuid();
    rig.Gateway.NextConfirmStatus("REQUIRES_CUSTOMER_ACTION");

    var first = await rig.Charge(id);
    first.Status.Should().Be(ChargeStatus.Pending);
    first.GatewayStatus.Should().Be("REQUIRES_CUSTOMER_ACTION");
    rig.Outbox.Events.Should().BeEmpty();

    var second = await rig.Charge(id);
    second.Status.Should().Be(ChargeStatus.Succeeded);
    rig.Ops("create").Should().ContainSingle();
  }

  [Fact]
  public async Task No_consent_is_NoConsent_without_any_gateway_call()
  {
    var rig = new Rig();
    var id = Guid.NewGuid();

    var res = await rig.Charge(id);

    res.Status.Should().Be(ChargeStatus.NoConsent);
    rig.Gateway.Calls.Should().BeEmpty();
    rig.Outbox.Events.Should().ContainSingle(e => e.Name == PaymentEvents.Failed);
  }

  [Fact]
  public async Task Subscription_consent_does_not_pay_a_penalty()
  {
    var rig = new Rig();
    rig.Customers.AddVerified("u1", ConsentPurpose.Subscription);

    (await rig.Charge(Guid.NewGuid())).Status.Should().Be(ChargeStatus.NoConsent);
  }

  [Fact]
  public async Task A_claimed_attempt_in_flight_blocks_a_second_caller()
  {
    var rig = new Rig();
    rig.Customers.AddVerified("u1");
    var id = Guid.NewGuid();
    await rig.Charges.EnsureLocked(new ChargeRecord
    {
      RequestId = id,
      UserId = "u1",
      Purpose = ConsentPurpose.Penalty,
      AmountCents = 500,
      Currency = "USD",
      State = ChargeState.Processing,
      Attempt = 1,
      InFlightUntil = DateTime.UtcNow.AddMinutes(1),
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow,
    }, CancellationToken.None);

    var res = await rig.Charge(id);

    res.Status.Should().Be(ChargeStatus.Pending);
    rig.Gateway.Calls.Should().BeEmpty();
  }

  [Fact]
  public async Task Same_request_id_with_other_terms_is_refused()
  {
    var rig = new Rig();
    rig.Customers.AddVerified("u1");
    var id = Guid.NewGuid();
    await rig.Charge(id, cents: 500);

    var act = () => rig.Charge(id, cents: 900);

    await act.Should().ThrowAsync<InvalidOperationException>();
  }

  [Fact]
  public async Task Charge_inside_a_transaction_is_refused()
  {
    var rig = new Rig();
    rig.Customers.AddVerified("u1");
    using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

    var act = () => rig.Charge(Guid.NewGuid());

    await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*outside any unit of work*");
  }

  [Fact]
  public async Task Webhook_success_settles_a_pending_charge_once()
  {
    var rig = new Rig();
    rig.Customers.AddVerified("u1");
    var id = Guid.NewGuid();
    rig.Gateway.NextConfirmStatus("REQUIRES_CUSTOMER_ACTION");
    var pending = await rig.Charge(id);

    (await rig.Service().SettleFromGateway(pending.IntentId!, CancellationToken.None)).Should().BeTrue();
    (await rig.Service().SettleFromGateway(pending.IntentId!, CancellationToken.None)).Should().BeFalse();

    rig.Charges.Rows[id].State.Should().Be(ChargeState.Succeeded);
    rig.Outbox.Events.Count(e => e.Name == PaymentEvents.Completed).Should().Be(1);
  }

  [Theory]
  [InlineData(1234L, "USD", 12.34)]
  [InlineData(500L, "JPY", 500)]
  public void Minor_units_convert_per_currency(long minor, string currency, decimal major) =>
    Money.ToMajor(minor, currency).Should().Be(major);
}
