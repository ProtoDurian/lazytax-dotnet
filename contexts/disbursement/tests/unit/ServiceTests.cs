#pragma warning disable xUnit1051 // the host calls take no test-scoped token; tests are short
using Ctx.Disbursement.Contract.V1;
using Ctx.Disbursement.Data;
using Ctx.Disbursement.Domain;
using LazyTax.Platform.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ctx.Disbursement.Tests.Unit;

internal sealed class NoUow : IUnitOfWork
{
  public Task<T> Start<T>(Func<Task<T>> work, CancellationToken ct = default) => work();
  public Task Start(Func<Task> work, CancellationToken ct = default) => work();
  public Task LockUser(string userId, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class MemRepo : IDisbursementRepository
{
  public readonly Dictionary<Guid, DisbursementInfo> Rows = [];

  public Task Insert(DisbursementInfo row, CancellationToken ct = default) { this.Rows[row.Id] = row; return Task.CompletedTask; }

  public Task<bool> MarkCompleted(Guid id, string providerDonationId, CancellationToken ct = default)
  {
    if (!this.Rows.TryGetValue(id, out var r) || r.Status == DisbursementStatus.Completed) return Task.FromResult(false);
    this.Rows[id] = r with { Status = DisbursementStatus.Completed, ProviderDonationId = providerDonationId };
    return Task.FromResult(true);
  }

  public Task<bool> MarkFailed(Guid id, string error, CancellationToken ct = default)
  {
    if (!this.Rows.TryGetValue(id, out var r) || r.Status == DisbursementStatus.Completed) return Task.FromResult(false);
    this.Rows[id] = r with { Status = DisbursementStatus.Failed, LastError = error, Attempts = r.Attempts + 1 };
    return Task.FromResult(true);
  }

  public Task<IReadOnlyList<DisbursementInfo>> GetReconcilable(DateTime cutoffUtc, int batchSize, CancellationToken ct = default) =>
    Task.FromResult<IReadOnlyList<DisbursementInfo>>(this.Rows.Values
      .Where(r => r.Status == DisbursementStatus.Pending && r.UpdatedAt < cutoffUtc).Take(batchSize).ToList());

  public Task<DisbursementInfo?> Get(Guid id, CancellationToken ct = default) => Task.FromResult(this.Rows.GetValueOrDefault(id));
  public Task<IReadOnlyList<DisbursementInfo>> Search(DisbursementSearch search, CancellationToken ct = default) => throw new NotSupportedException();

  /// <summary>Makes every Pending row look old enough to reconcile (the lease passed).</summary>
  public void AgePending()
  {
    foreach (var (id, r) in this.Rows.ToList())
      if (r.Status == DisbursementStatus.Pending) this.Rows[id] = r with { UpdatedAt = r.UpdatedAt - DisbursementService.ReconcileLease - TimeSpan.FromMinutes(1) };
  }
}

public sealed class ServiceTests
{
  private static readonly DonorIdentity Donor = new("LazyTax", "Donations", "donations@lazytax.club");

  private readonly FakePenalty penalty = new();
  private readonly FakeTargets targets = new();
  private readonly MemRepo repo = new();
  private readonly FakeDonationGateway gateway = new();
  private readonly DisbursementService service;

  public ServiceTests() =>
    this.service = new DisbursementService(new NoUow(), this.penalty, this.targets, this.repo, this.gateway,
      NullLogger<DisbursementService>.Instance);

  private (Guid Charity, string Org) Charity(long cents, string currency = "USD")
  {
    var c = Guid.NewGuid();
    var org = this.targets.Add(c);
    this.penalty.AddCharged(c, currency, cents);
    return (c, org);
  }

  private DisbursementInfo Only(Guid charity) => this.repo.Rows.Values.Single(r => r.CharityId == charity);

  [Fact]
  public async Task Claims_donates_and_completes()
  {
    var (c, org) = this.Charity(1250);
    (await this.service.ProcessPending(Donor, 0, 10, default)).Should().Be(1);
    var d = Only(c);
    d.Status.Should().Be(DisbursementStatus.Completed);
    d.AmountCents.Should().Be(1250);
    d.PledgeOrganizationId.Should().Be(org);
    d.ProviderDonationId.Should().NotBeNullOrEmpty();
    this.penalty.ClaimedBy(d.Id).Should().Be(1250);
    this.gateway.Requests.Should().ContainSingle(r => r.OrganizationId == org && r.Reference == d.Id.ToString() && r.DonorEmail == Donor.Email);
  }

  [Fact]
  public async Task Group_below_minimum_is_left_alone()
  {
    var (c, org) = this.Charity(500);
    (await this.service.ProcessPending(Donor, 1000, 10, default)).Should().Be(0);
    this.repo.Rows.Values.Should().NotContain(r => r.CharityId == c);
    this.penalty.Unclaimed(c).Should().Be(500);
    this.gateway.Requests.Should().NotContain(r => r.OrganizationId == org);
  }

  [Fact]
  public async Task Charity_without_pledge_org_is_skipped_not_failed()
  {
    var c = Guid.NewGuid();
    this.penalty.AddCharged(c, "USD", 900);
    (await this.service.ProcessPending(Donor, 0, 10, default)).Should().Be(0);
    this.repo.Rows.Should().BeEmpty();
    this.penalty.Unclaimed(c).Should().Be(900);
  }

  [Fact]
  public async Task Each_currency_is_its_own_payout()
  {
    var c = Guid.NewGuid();
    this.targets.Add(c);
    this.penalty.AddCharged(c, "USD", 300);
    this.penalty.AddCharged(c, "SGD", 400);
    (await this.service.ProcessPending(Donor, 0, 10, default)).Should().Be(2);
    this.repo.Rows.Values.Select(r => (r.Currency, r.AmountCents)).Should().BeEquivalentTo([("USD", 300L), ("SGD", 400L)]);
  }

  [Fact]
  public async Task Max_groups_caps_the_pass_and_the_rest_wait()
  {
    var big = this.Charity(5000);
    var small = this.Charity(100);
    (await this.service.ProcessPending(Donor, 0, 1, default)).Should().Be(1);
    this.Only(big.Charity).Status.Should().Be(DisbursementStatus.Completed);
    this.penalty.Unclaimed(small.Charity).Should().Be(100);
  }

  [Fact]
  public async Task Second_pass_does_not_pay_the_same_penalties_again()
  {
    this.Charity(700);
    await this.service.ProcessPending(Donor, 0, 10, default);
    (await this.service.ProcessPending(Donor, 0, 10, default)).Should().Be(0);
    this.gateway.Requests.Should().ContainSingle();
  }

  [Fact]
  public async Task Definitive_rejection_fails_the_payout_and_releases_the_penalties()
  {
    var (c, org) = this.Charity(800);
    this.gateway.Script(org, FakeOutcome.Reject);
    (await this.service.ProcessPending(Donor, 0, 10, default)).Should().Be(0);
    var d = Only(c);
    d.Status.Should().Be(DisbursementStatus.Failed);
    d.Attempts.Should().Be(1);
    d.LastError.Should().Contain("refused");
    this.penalty.Unclaimed(c).Should().Be(800);
  }

  [Fact]
  public async Task Ambiguous_failure_stays_pending_and_keeps_the_penalties_claimed()
  {
    var (c, org) = this.Charity(800);
    this.gateway.Script(org, FakeOutcome.TimeoutAfterCreate);
    (await this.service.ProcessPending(Donor, 0, 10, default)).Should().Be(0);
    var d = Only(c);
    d.Status.Should().Be(DisbursementStatus.Pending);
    this.penalty.Unclaimed(c).Should().Be(0, "releasing now could double-pay a donation that landed");
    this.penalty.Releases.Should().Be(0);
  }

  [Fact]
  public async Task Reconcile_completes_a_donation_that_landed_without_donating_again()
  {
    var (c, org) = this.Charity(800);
    this.gateway.Script(org, FakeOutcome.TimeoutAfterCreate);
    await this.service.ProcessPending(Donor, 0, 10, default);
    this.repo.AgePending();
    this.gateway.Script(org, FakeOutcome.Succeed);

    (await this.service.ProcessPending(Donor, 0, 10, default)).Should().Be(0); // reconcile is not counted as a new payout
    var d = Only(c);
    d.Status.Should().Be(DisbursementStatus.Completed);
    d.ProviderDonationId.Should().NotBeNullOrEmpty();
    this.gateway.Requests.Count(r => r.OrganizationId == org).Should().Be(1);
  }

  [Fact]
  public async Task Reconcile_releases_when_the_donation_conclusively_never_landed()
  {
    var (c, org) = this.Charity(800);
    this.gateway.Script(org, FakeOutcome.TimeoutBeforeCreate);
    await this.service.ProcessPending(Donor, 0, 10, default);
    this.repo.AgePending();
    this.gateway.Script(org, FakeOutcome.Reject); // the retry in the same pass must not succeed by accident

    await this.service.ProcessPending(Donor, 0, 10, default);
    this.repo.Rows.Values.Where(r => r.CharityId == c).Select(r => r.Status).Should().OnlyContain(s => s == DisbursementStatus.Failed);
    this.penalty.Unclaimed(c).Should().Be(800);
  }

  [Fact]
  public async Task Reconcile_with_an_inconclusive_lookup_leaves_it_pending()
  {
    var (c, org) = this.Charity(800);
    this.gateway.Script(org, FakeOutcome.TimeoutBeforeCreate);
    await this.service.ProcessPending(Donor, 0, 10, default);
    var id = Only(c).Id;
    this.repo.AgePending();
    this.gateway.MakeLookupInconclusive(id.ToString());

    await this.service.ProcessPending(Donor, 0, 10, default);
    this.repo.Rows[id].Status.Should().Be(DisbursementStatus.Pending);
    this.penalty.Releases.Should().Be(0);
  }

  [Fact]
  public async Task A_young_pending_payout_is_not_reconciled()
  {
    var (c, org) = this.Charity(800);
    this.gateway.Script(org, FakeOutcome.TimeoutBeforeCreate);
    await this.service.ProcessPending(Donor, 0, 10, default);
    await this.service.ProcessPending(Donor, 0, 10, default); // lease not over
    Only(c).Status.Should().Be(DisbursementStatus.Pending);
  }

  [Fact]
  public async Task One_rejected_group_does_not_stop_the_others()
  {
    var bad = this.Charity(900);
    var good = this.Charity(100);
    this.gateway.Script(bad.Org, FakeOutcome.Reject);
    (await this.service.ProcessPending(Donor, 0, 10, default)).Should().Be(1);
    Only(good.Charity).Status.Should().Be(DisbursementStatus.Completed);
    Only(bad.Charity).Status.Should().Be(DisbursementStatus.Failed);
  }

  [Fact]
  public async Task A_completed_payout_is_never_undone()
  {
    var (c, _) = this.Charity(800);
    await this.service.ProcessPending(Donor, 0, 10, default);
    var id = Only(c).Id;
    (await this.repo.MarkFailed(id, "late failure")).Should().BeFalse();
    (await this.repo.MarkCompleted(id, "other")).Should().BeFalse();
    this.repo.Rows[id].Status.Should().Be(DisbursementStatus.Completed);
  }
}
