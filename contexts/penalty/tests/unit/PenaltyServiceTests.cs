using Ctx.Penalty.Contract.V1;
using Ctx.Penalty.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;

namespace Ctx.Penalty.Tests.Unit;

public sealed class StakeBindingTests
{
  [Fact]
  public async Task Missed_with_stake_and_charity_enqueues_exact_cents()
  {
    var h = new Harness();
    var e = Harness.Missed("u1", stake: 1999, bps: 5_000);
    (await h.Service.OnSettled(e, Ct)).Should().BeTrue();
    var p = h.Store.Rows.Single();
    p.AmountCents.Should().Be(999); // 1999 * 5000 / 10000, truncated
    p.Currency.Should().Be("USD");
    p.Status.Should().Be(PenaltyStatus.Pending);
    p.HabitExecutionId.Should().Be(e.ExecutionId!.Value);
  }

  [Fact]
  public async Task Same_execution_twice_enqueues_once()
  {
    var h = new Harness();
    var e = Harness.Missed("u1");
    await h.Service.OnSettled(e, Ct);
    (await h.Service.OnSettled(e, Ct)).Should().BeFalse();
    h.Store.Rows.Should().HaveCount(1);
  }

  [Theory]
  [InlineData("done")]
  [InlineData("frozen")]
  [InlineData("vacation")]
  [InlineData("skipped")]
  public async Task Other_outcomes_enqueue_nothing(string status)
  {
    var h = new Harness();
    await h.Service.OnSettled(Harness.Missed("u1") with { Status = status }, Ct);
    h.Store.Rows.Should().BeEmpty();
  }

  [Fact]
  public async Task No_money_or_no_charity_or_no_execution_enqueues_nothing()
  {
    var h = new Harness();
    await h.Service.OnSettled(Harness.Missed("u1", stake: 0), Ct);
    await h.Service.OnSettled(Harness.Missed("u1", stake: 1, bps: 100), Ct); // rounds to 0 cents
    await h.Service.OnSettled(Harness.Missed("u1") with { CharityId = null }, Ct);
    await h.Service.OnSettled(Harness.Missed("u1") with { ExecutionId = null }, Ct);
    h.Store.Rows.Should().BeEmpty();
  }
}

public sealed class DrainTests
{
  private static async Task<Harness> WithPending(int n = 1)
  {
    var h = new Harness();
    for (var i = 0; i < n; i++) await h.Service.OnSettled(Harness.Missed("u1"), Ct);
    return h;
  }

  [Fact]
  public async Task Succeeded_charges_credits_and_emits_once()
  {
    var h = await WithPending();
    (await h.Service.ProcessPending(Ct)).Should().Be(1);
    var p = h.Store.Rows.Single();
    p.Status.Should().Be(PenaltyStatus.Charged);
    p.PaymentIntentId.Should().StartWith("int_");
    h.Store.Balances[(p.CharityId, "USD")].Should().Be(1000);
    var (Name, Payload) = h.Outbox.Events.Should().ContainSingle().Subject;
    Name.Should().Be(PenaltyEvents.Charged);
    ((PenaltyCharged)Payload).CharityName.Should().StartWith("Charity ");

    // A late payment.completed for the same penalty changes nothing.
    (await h.Service.Charged(p.Id, "int_other", Ct)).Should().BeFalse();
    h.Store.Balances[(p.CharityId, "USD")].Should().Be(1000);
    h.Outbox.Events.Should().HaveCount(1);
  }

  [Fact]
  public async Task Pending_retries_then_fails_at_the_cap()
  {
    var h = await WithPending();
    h.Gateway.Next = _ => new ChargeOutcome.Pending("int_1", "REQUIRES_CUSTOMER_ACTION");
    (await h.Service.ProcessPending(Ct)).Should().Be(0);
    h.Store.Rows.Single().Should().Match<PenaltyInfo>(p => p.Status == PenaltyStatus.Pending && p.Attempts == 1 && p.PaymentIntentId == "int_1");
    await h.Service.ProcessPending(Ct);
    (await h.Service.ProcessPending(Ct)).Should().Be(1); // attempt 3 of 3
    var failed = h.Store.Rows.Single();
    failed.Status.Should().Be(PenaltyStatus.Failed);
    failed.LastError.Should().Contain("REQUIRES_CUSTOMER_ACTION");
    h.Outbox.Events.Should().ContainSingle(e => e.Name == PenaltyEvents.Failed);
  }

  [Fact]
  public async Task No_consent_is_skipped_without_retry()
  {
    var h = await WithPending();
    h.Gateway.Next = _ => new ChargeOutcome.NoConsent();
    (await h.Service.ProcessPending(Ct)).Should().Be(1);
    h.Store.Rows.Single().Status.Should().Be(PenaltyStatus.Skipped);
    (await h.Service.ProcessPending(Ct)).Should().Be(0);
    h.Gateway.Calls.Should().Be(1);
    h.Outbox.Events.Should().BeEmpty();
  }

  [Fact]
  public async Task Errors_bump_then_fail_at_the_cap()
  {
    var h = await WithPending();
    h.Gateway.Next = _ => new ChargeOutcome.Error("declined");
    await h.Service.ProcessPending(Ct);
    h.Store.Rows.Single().Should().Match<PenaltyInfo>(p => p.Status == PenaltyStatus.Pending && p.Attempts == 1 && p.LastError == "declined");
    await h.Service.ProcessPending(Ct);
    await h.Service.ProcessPending(Ct);
    h.Store.Rows.Single().Status.Should().Be(PenaltyStatus.Failed);
    h.Outbox.Events.Should().ContainSingle(e => e.Name == PenaltyEvents.Failed);
  }

  [Fact]
  public async Task A_throwing_charge_is_isolated_and_the_batch_continues()
  {
    var h = await WithPending(3);
    var bad = h.Store.Rows[1].Id;
    h.Gateway.Next = p => p.Id == bad ? throw new InvalidOperationException("boom") : new ChargeOutcome.Succeeded("int");
    (await h.Service.ProcessPending(Ct)).Should().Be(2);
    h.Store.Rows.Count(r => r.Status == PenaltyStatus.Charged).Should().Be(2);
    var b = h.Store.Rows.Single(r => r.Id == bad);
    b.Status.Should().Be(PenaltyStatus.Pending);
    b.LastError.Should().Contain("boom");
  }

  [Fact]
  public async Task Charged_rows_are_never_charged_again()
  {
    var h = await WithPending();
    await h.Service.ProcessPending(Ct);
    await h.Service.ProcessPending(Ct);
    h.Gateway.Calls.Should().Be(1);
  }
}

public sealed class DebtTests
{
  [Fact]
  public async Task Outstanding_is_every_uncharged_penalty_per_currency()
  {
    var h = new Harness();
    await h.Service.OnSettled(Harness.Missed("u1", stake: 500), Ct);
    await h.Service.OnSettled(Harness.Missed("u1", stake: 700), Ct);
    await h.Service.OnSettled(Harness.Missed("u1", stake: 300, currency: "sgd"), Ct);
    await h.Service.OnSettled(Harness.Missed("u2", stake: 900), Ct);
    var d = await h.Service.Outstanding("u1", Ct);
    d.TotalCents.Should().Be(1200);
    d.Currency.Should().Be("USD");
    d.ByCurrency.Should().BeEquivalentTo([new CurrencyAmount("USD", 1200), new CurrencyAmount("SGD", 300)]);
    d.Count.Should().Be(3);
  }

  [Fact]
  public async Task Charged_penalties_are_not_debt()
  {
    var h = new Harness();
    await h.Service.OnSettled(Harness.Missed("u1"), Ct);
    await h.Service.ProcessPending(Ct);
    var d = await h.Service.Outstanding("u1", Ct);
    d.TotalCents.Should().Be(0);
    d.Currency.Should().BeEmpty();
    d.ByCurrency.Should().BeEmpty();
    d.Count.Should().Be(0);
    await h.Service.GuardNoDebt("u1", Ct);
  }

  [Fact]
  public async Task Guard_blocks_deletion_with_409_while_owing()
  {
    var h = new Harness();
    await h.Service.OnSettled(Harness.Missed("u1", stake: 1050), Ct);
    var act = () => h.Service.GuardNoDebt("u1", Ct);
    var ex = (await act.Should().ThrowAsync<AccountDeletionBlocked>()).Which;
    ex.Status.Should().Be(409);
    ex.Id.Should().Be("account_deletion_blocked");
    ex.Message.Should().Contain("10.50 USD");
  }

  [Fact]
  public async Task Guard_is_off_when_the_flag_is_off()
  {
    var h = new Harness();
    h.Options.BlockDeletionOnDebt = false;
    await h.Service.OnSettled(Harness.Missed("u1"), Ct);
    await h.Service.GuardNoDebt("u1", Ct);
  }

  [Fact]
  public async Task Purge_anonymises_and_keeps_the_ledger()
  {
    var h = new Harness();
    await h.Service.OnSettled(Harness.Missed("u1"), Ct);
    await h.Store.Anonymise("u1", Ct);
    h.Store.Rows.Single().UserId.Should().Be(Stake.DeletedUser);
    h.Store.Rows.Single().AmountCents.Should().Be(1000);
  }
}

public sealed class SearchTests
{
  [Theory]
  [InlineData("paid", 20, 0)]
  [InlineData(null, 0, 0)]
  [InlineData(null, 101, 0)]
  [InlineData(null, 20, -1)]
  public async Task Bad_queries_are_400(string? status, int limit, int skip)
  {
    var h = new Harness();
    var act = () => h.Service.Search(Owner.User("u1"), new PenaltySearch(status, limit, skip), Ct);
    (await act.Should().ThrowAsync<ValidationFailed>()).Which.Status.Should().Be(400);
  }

  [Fact]
  public async Task GetOwned_of_another_users_penalty_is_404()
  {
    var h = new Harness();
    await h.Service.OnSettled(Harness.Missed("a"), Ct);
    var id = h.Store.Rows.Single().Id;
    var act = () => h.Service.GetOwned(Owner.User("b"), id, Ct);
    (await act.Should().ThrowAsync<NotFound>()).Which.Status.Should().Be(404);
  }
}
