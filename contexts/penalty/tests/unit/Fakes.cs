using Ctx.Penalty.Contract.V1;
using Ctx.Penalty.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ctx.Penalty.Tests.Unit;

internal sealed class FakeUow : IUnitOfWork
{
  public Task<T> Start<T>(Func<Task<T>> work, CancellationToken ct = default) => work();
  public Task Start(Func<Task> work, CancellationToken ct = default) => work();
  public Task LockUser(string userId, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class FakeOutbox : IOutbox
{
  public List<(string Name, object Payload)> Events { get; } = [];

  public Task Add(string eventName, object payload, CancellationToken ct = default)
  {
    this.Events.Add((eventName, payload));
    return Task.CompletedTask;
  }
}

internal sealed class FakeGateway : IChargeGateway
{
  public Func<PenaltyInfo, ChargeOutcome> Next { get; set; } = p => new ChargeOutcome.Succeeded("int_" + p.Id.ToString("N")[..8]);
  public int Calls { get; private set; }

  public Task<ChargeOutcome> Charge(PenaltyInfo penalty, CancellationToken ct)
  {
    this.Calls++;
    return Task.FromResult(this.Next(penalty));
  }
}

internal sealed class FakeNames : ICharityNames
{
  public Task<string?> NameOf(Guid charityId, CancellationToken ct) => Task.FromResult<string?>("Charity " + charityId.ToString("N")[..4]);
}

/// <summary>In-memory IPenaltyStore with the same rules as the SQL one (claim holds, idempotent charge).</summary>
internal sealed class MemoryStore : IPenaltyStore
{
  public List<PenaltyInfo> Rows { get; } = [];
  public Dictionary<(Guid, string), long> Balances { get; } = [];

  private void Set(Guid id, Func<PenaltyInfo, PenaltyInfo> f)
  {
    var i = this.Rows.FindIndex(r => r.Id == id);
    this.Rows[i] = f(this.Rows[i]);
  }

  private bool IfClaimed(Guid id, Func<PenaltyInfo, PenaltyInfo> f)
  {
    var r = this.Rows.FirstOrDefault(x => x.Id == id);
    if (r is null || r.Status != PenaltyStatus.Processing) return false;
    this.Set(id, f);
    return true;
  }

  public Task<bool> Enqueue(NewPenalty p, DateTime now, CancellationToken ct)
  {
    if (this.Rows.Any(r => r.HabitExecutionId == p.HabitExecutionId)) return Task.FromResult(false);
    this.Rows.Add(new PenaltyInfo(Guid.NewGuid(), p.HabitExecutionId, p.HabitId, p.UserId, p.CharityId, p.AmountCents,
      p.Currency, PenaltyStatus.Pending, null, 0, null, null, now, now));
    return Task.FromResult(true);
  }

  public Task<IReadOnlyList<PenaltyInfo>> ClaimPending(int batchSize, DateTime staleBefore, DateTime now, CancellationToken ct)
  {
    var due = this.Rows.Where(r => r.Status == PenaltyStatus.Pending || (r.Status == PenaltyStatus.Processing && r.UpdatedAt < staleBefore))
      .OrderBy(r => r.CreatedAt).Take(batchSize).ToList();
    foreach (var r in due) this.Set(r.Id, x => x with { Status = PenaltyStatus.Processing, UpdatedAt = now });
    return Task.FromResult<IReadOnlyList<PenaltyInfo>>([.. due.Select(r => this.Rows.First(x => x.Id == r.Id))]);
  }

  public Task<PenaltyInfo?> MarkCharged(Guid id, string paymentIntentId, DateTime now, CancellationToken ct)
  {
    var r = this.Rows.FirstOrDefault(x => x.Id == id);
    if (r is null || r.Status == PenaltyStatus.Charged) return Task.FromResult<PenaltyInfo?>(null);
    this.Set(id, x => x with { Status = PenaltyStatus.Charged, PaymentIntentId = paymentIntentId, LastError = null, UpdatedAt = now });
    this.Balances[(r.CharityId, r.Currency)] = this.Balances.GetValueOrDefault((r.CharityId, r.Currency)) + r.AmountCents;
    return Task.FromResult<PenaltyInfo?>(this.Rows.First(x => x.Id == id));
  }

  public Task<bool> MarkPending(Guid id, string? paymentIntentId, int attempts, DateTime now, CancellationToken ct) =>
    Task.FromResult(this.IfClaimed(id, x => x with
    {
      Status = PenaltyStatus.Pending,
      PaymentIntentId = paymentIntentId ?? x.PaymentIntentId,
      Attempts = attempts,
      UpdatedAt = now,
    }));

  public Task<bool> Bump(Guid id, string error, DateTime now, CancellationToken ct) =>
    Task.FromResult(this.IfClaimed(id, x => x with { Status = PenaltyStatus.Pending, Attempts = x.Attempts + 1, LastError = error, UpdatedAt = now }));

  public Task<bool> MarkFailed(Guid id, string error, DateTime now, CancellationToken ct) =>
    Task.FromResult(this.IfClaimed(id, x => x with { Status = PenaltyStatus.Failed, Attempts = x.Attempts + 1, LastError = error, UpdatedAt = now }));

  public Task<bool> MarkSkipped(Guid id, DateTime now, CancellationToken ct) =>
    Task.FromResult(this.IfClaimed(id, x => x with { Status = PenaltyStatus.Skipped, UpdatedAt = now }));

  public Task<PenaltyInfo?> Get(Owner owner, Guid id, CancellationToken ct) =>
    Task.FromResult(this.Rows.FirstOrDefault(r => r.Id == id && (owner.IsAnyone || r.UserId == owner.UserId)));

  public Task<IReadOnlyList<PenaltyInfo>> Search(Owner owner, PenaltySearch search, CancellationToken ct) =>
    Task.FromResult<IReadOnlyList<PenaltyInfo>>([.. this.Rows.Where(r => owner.IsAnyone || r.UserId == owner.UserId)
      .Where(r => search.Status is null || r.Status == search.Status).OrderByDescending(r => r.CreatedAt).Skip(search.Skip).Take(search.Limit)]);

  public Task<IReadOnlyList<(string Currency, long TotalCents, int Count)>> Unpaid(string userId, CancellationToken ct) =>
    Task.FromResult<IReadOnlyList<(string, long, int)>>([.. this.Rows.Where(r => r.UserId == userId && r.Status != PenaltyStatus.Charged)
      .GroupBy(r => r.Currency).OrderBy(g => g.Min(r => r.CreatedAt)).Select(g => (g.Key, g.Sum(r => r.AmountCents), g.Count()))]);

  public Task<IReadOnlyList<CharityBalance>> CharityBalances(CancellationToken ct) =>
    Task.FromResult<IReadOnlyList<CharityBalance>>([.. this.Balances.Select(b => new CharityBalance(b.Key.Item1, b.Key.Item2, b.Value, DateTime.UtcNow))]);

  public Task<IReadOnlyList<PayoutGroup>> PayoutCandidates(long minPayoutCents, CancellationToken ct) => throw new NotSupportedException();
  public Task<long> ClaimForPayout(Guid disbursementId, Guid charityId, string currency, DateTime now, CancellationToken ct) => throw new NotSupportedException();
  public Task ReleasePayout(Guid disbursementId, DateTime now, CancellationToken ct) => throw new NotSupportedException();

  public Task<int> Anonymise(string userId, CancellationToken ct)
  {
    var n = 0;
    foreach (var r in this.Rows.Where(r => r.UserId == userId).ToList())
    {
      this.Set(r.Id, x => x with { UserId = Stake.DeletedUser });
      n++;
    }
    return Task.FromResult(n);
  }
}

internal sealed class Harness
{
  public MemoryStore Store { get; } = new();
  public FakeGateway Gateway { get; } = new();
  public FakeOutbox Outbox { get; } = new();
  public PenaltyOptions Options { get; } = new() { MaxAttempts = 3 };
  public PenaltyService Service { get; }

  public Harness() =>
    this.Service = new PenaltyService(this.Store, this.Gateway, new FakeNames(), new FakeUow(), this.Outbox, this.Options,
      TimeProvider.System, NullLogger<PenaltyService>.Instance);

  public static SettledCheck Missed(string userId, long stake = 1000, long bps = 10_000, Guid? charity = null, string currency = "usd") =>
    new(userId, Guid.NewGuid(), Guid.NewGuid(), "missed", stake, bps, currency, charity ?? Guid.NewGuid());
}
