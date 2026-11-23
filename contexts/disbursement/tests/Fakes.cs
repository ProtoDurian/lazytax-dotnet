using Ctx.Disbursement.Contract.V1;
using Ctx.Disbursement.Domain;
using Ctx.Penalty.Contract.V1;
using LazyTax.Platform.Data;

namespace Ctx.Disbursement.Tests;

/// <summary>An in-memory penalty ledger: charged penalties of (charity, currency), claimed by payouts.</summary>
public sealed class FakePenalty : IPenaltyV1
{
  public sealed record Row(Guid Id, Guid CharityId, string Currency, long Cents, Guid? DisbursementId);

  private readonly List<Row> rows = [];
  public bool ThrowOnRelease { get; set; }
  public int Releases { get; private set; }

  public void AddCharged(Guid charity, string currency, long cents) =>
    this.rows.Add(new Row(Guid.NewGuid(), charity, currency, cents, null));

  public long ClaimedBy(Guid disbursement) => this.rows.Where(r => r.DisbursementId == disbursement).Sum(r => r.Cents);

  public long Unclaimed(Guid charity) => this.rows.Where(r => r.CharityId == charity && r.DisbursementId is null).Sum(r => r.Cents);

  public Task<IReadOnlyList<PayoutGroup>> PayoutCandidates(long minPayoutCents, CancellationToken ct = default) =>
    Task.FromResult<IReadOnlyList<PayoutGroup>>(this.rows.Where(r => r.DisbursementId is null)
      .GroupBy(r => (r.CharityId, r.Currency))
      .Select(g => new PayoutGroup(g.Key.CharityId, g.Key.Currency, g.Sum(x => x.Cents)))
      .Where(g => g.AmountCents >= minPayoutCents).OrderByDescending(g => g.AmountCents).ToList());

  public Task<long> ClaimForPayout(Guid disbursementId, Guid charityId, string currency, CancellationToken ct = default)
  {
    long sum = 0;
    for (var i = 0; i < this.rows.Count; i++)
    {
      var r = this.rows[i];
      if (r.CharityId != charityId || r.Currency != currency || r.DisbursementId is not null) continue;
      this.rows[i] = r with { DisbursementId = disbursementId };
      sum += r.Cents;
    }
    return Task.FromResult(sum);
  }

  public Task ReleasePayout(Guid disbursementId, CancellationToken ct = default)
  {
    if (this.ThrowOnRelease) throw new InvalidOperationException("fake penalty: release failed");
    this.Releases++;
    for (var i = 0; i < this.rows.Count; i++)
      if (this.rows[i].DisbursementId == disbursementId) this.rows[i] = this.rows[i] with { DisbursementId = null };
    return Task.CompletedTask;
  }

  public Task<OutstandingDebt> Outstanding(string userId, CancellationToken ct = default) => throw new NotSupportedException();
  public Task<IReadOnlyList<CharityBalance>> CharityBalances(CancellationToken ct = default) => throw new NotSupportedException();
  public Task<IReadOnlyList<PenaltyInfo>> Search(Owner owner, PenaltySearch search, CancellationToken ct = default) => throw new NotSupportedException();
  public Task<PenaltyInfo?> Get(Owner owner, Guid id, CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>Charity -> Pledge organisation id. A charity not added has none.</summary>
public sealed class FakeTargets : IPayoutTargets
{
  private readonly Dictionary<Guid, string> orgs = [];

  public string Add(Guid charity) => this.orgs[charity] = "org_" + Guid.NewGuid().ToString("N")[..10];

  public Task<string?> PledgeOrganizationId(Guid charityId, CancellationToken ct = default) =>
    Task.FromResult(this.orgs.GetValueOrDefault(charityId));
}
