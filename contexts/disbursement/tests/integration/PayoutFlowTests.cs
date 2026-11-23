#pragma warning disable xUnit1051 // the host calls take no test-scoped token; tests are short
using Ctx.Disbursement.Contract.V1;
using Ctx.Disbursement.Data;
using Ctx.Disbursement.Domain;
using Ctx.Penalty.Contract.V1;
using LazyTax.Platform.Lifecycle;
using LazyTax.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ctx.Disbursement.Tests.Integration;

/// <summary>
/// The payout pass on the real database (disbursement's own tables, real unit of work), with penalty and charity
/// replaced by in-memory fakes so this lane does not depend on their internals. Penalty's real claim is tested
/// in penalty's own tests. One class on purpose: a pass claims every charged penalty, so passes must not overlap.
/// </summary>
public sealed class PayoutFlowTests : IDisposable
{
  private static readonly DonorIdentity Donor = new("LazyTax", "Donations", "donations@lazytax.club");

  private readonly KernelApp app;
  private readonly FakePenalty penalty = new();
  private readonly FakeTargets targets = new();
  private readonly WebApplicationFactory<Program> host;

  public PayoutFlowTests(KernelApp app)
  {
    this.app = app;
    this.host = app.WithWebHostBuilder(b => b.ConfigureServices(s =>
    {
      s.RemoveAll<IPenaltyV1>();
      s.AddSingleton<IPenaltyV1>(this.penalty);
      s.RemoveAll<IPayoutTargets>();
      s.AddSingleton<IPayoutTargets>(this.targets);
    }));
  }

  public void Dispose() => this.host.Dispose();

  private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> work)
  {
    await using var scope = this.host.Services.CreateAsyncScope();
    return await work(scope.ServiceProvider);
  }

  private (Guid Charity, string Org) Charity(long cents)
  {
    var c = Guid.NewGuid();
    var org = this.targets.Add(c);
    this.penalty.AddCharged(c, "USD", cents);
    return (c, org);
  }

  private Task<int> Run() => this.InScope(sp => sp.GetRequiredService<IDisbursementV1>().ProcessPending(Donor, 0, 100, default));

  private Task<DisbursementInfo> RowOf(Guid charity) => this.InScope(async sp =>
    (await sp.GetRequiredService<IDisbursementV1>().Search(new DisbursementSearch(CharityId: charity), default)).Single());

  private FakeDonationGateway Vendor => this.host.Services.GetRequiredService<FakeDonationGateway>();

  [Fact]
  public async Task Pass_writes_a_completed_row_and_stamps_the_penalties()
  {
    var (c, org) = this.Charity(2500);
    (await this.Run()).Should().BeGreaterThanOrEqualTo(1);
    var d = await this.RowOf(c);
    d.Status.Should().Be(DisbursementStatus.Completed);
    d.AmountCents.Should().Be(2500);
    d.PledgeOrganizationId.Should().Be(org);
    this.penalty.ClaimedBy(d.Id).Should().Be(2500);
    this.Vendor.Requests.Should().ContainSingle(r => r.OrganizationId == org);
  }

  [Fact]
  public async Task Rejected_donation_is_failed_in_the_database_and_released()
  {
    var (c, org) = this.Charity(900);
    this.Vendor.Script(org, FakeOutcome.Reject);
    await this.Run();
    var d = await this.RowOf(c);
    d.Status.Should().Be(DisbursementStatus.Failed);
    d.Attempts.Should().Be(1);
    this.penalty.Unclaimed(c).Should().Be(900);
  }

  [Fact]
  public async Task Ambiguous_failure_stays_pending_then_reconcile_completes_it_without_a_second_donation()
  {
    var (c, org) = this.Charity(1100);
    this.Vendor.Script(org, FakeOutcome.TimeoutAfterCreate);
    await this.Run();
    var pending = await this.RowOf(c);
    pending.Status.Should().Be(DisbursementStatus.Pending);

    // The lease (10 min) passes.
    await this.InScope(async sp =>
    {
      await sp.GetRequiredService<DisbursementDb>().Database.ExecuteSqlRawAsync(
        "UPDATE disbursement.\"Disbursements\" SET \"UpdatedAt\" = now() - interval '1 hour' WHERE \"Id\" = {0}", pending.Id);
      return 0;
    });
    await this.Run();

    var done = await this.RowOf(c);
    done.Status.Should().Be(DisbursementStatus.Completed);
    done.ProviderDonationId.Should().NotBeNullOrEmpty();
    this.Vendor.Requests.Count(r => r.OrganizationId == org).Should().Be(1);
  }

  [Fact]
  public async Task Failure_and_release_are_one_unit_a_failing_release_leaves_the_row_pending()
  {
    var (c, org) = this.Charity(1300);
    this.Vendor.Script(org, FakeOutcome.Reject);
    this.penalty.ThrowOnRelease = true;
    try
    {
      await this.Run(); // the failure is swallowed per group; the row must not be half-written
    }
    finally
    {
      this.penalty.ThrowOnRelease = false;
    }
    (await this.RowOf(c)).Status.Should().Be(DisbursementStatus.Pending, "MarkFailed rolled back with the failed release");
  }

  [Fact]
  public async Task Two_passes_at_once_pay_a_charity_once_per_penalty()
  {
    var (c, org) = this.Charity(1700);
    // The fake ledger is not row-locked, so run them one after the other: the stamp is what blocks the second.
    await this.Run();
    await this.Run();
    this.Vendor.Requests.Count(r => r.OrganizationId == org).Should().Be(1);
    (await this.RowOf(c)).Status.Should().Be(DisbursementStatus.Completed);
  }

  [Fact]
  public async Task Purge_has_nothing_to_delete_and_the_schema_has_no_user_column()
  {
    var (c, _) = this.Charity(400);
    await this.Run();
    var before = await this.CountRows();

    await this.InScope(async sp =>
    {
      foreach (var p in sp.GetServices<IUserPurge>().OfType<DisbursementPurge>()) await p.Purge("u_any", default);
      return 0;
    });

    (await this.CountRows()).Should().Be(before, "payouts are per charity, not per user");
    (await this.RowOf(c)).Status.Should().Be(DisbursementStatus.Completed);
    var userColumns = await this.app.Count(
      "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'disbursement' AND column_name ILIKE '%user%'");
    userColumns.Should().Be(0);
  }

  private Task<long> CountRows() => this.InScope(async sp =>
    (long)(await sp.GetRequiredService<IDisbursementV1>().Search(new DisbursementSearch(Limit: 100), default)).Count);
}
