using System.Collections.Concurrent;
using Ctx.Payment.Contract.V1;
using Ctx.Payment.Data;
using Ctx.Payment.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ctx.Payment.Tests.Unit;

internal sealed class InlineUow : IUnitOfWork
{
  public Task<T> Start<T>(Func<Task<T>> work, CancellationToken ct = default) => work();

  public Task Start(Func<Task> work, CancellationToken ct = default) => work();

  public Task LockUser(string userId, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class ListOutbox : IOutbox
{
  public List<(string Name, object Payload)> Events { get; } = [];

  public Task Add(string eventName, object payload, CancellationToken ct = default)
  {
    this.Events.Add((eventName, payload));
    return Task.CompletedTask;
  }
}

internal sealed class MemCustomers : ICustomerRepository
{
  public ConcurrentDictionary<string, PaymentCustomer> ByUser { get; } = new();

  public void AddVerified(string userId, ConsentPurpose purpose = ConsentPurpose.Penalty, string consentId = "cst_1") =>
    this.ByUser[userId] = new PaymentCustomer(Guid.NewGuid(), userId, "cus_" + userId,
      new Dictionary<ConsentPurpose, StoredConsent> { [purpose] = new(consentId, ConsentStatus.Verified) },
      DateTime.UtcNow, DateTime.UtcNow);

  public Task<PaymentCustomer?> Get(Owner owner, CancellationToken ct) => Task.FromResult(this.ByUser.GetValueOrDefault(owner.UserId!));

  public Task<PaymentCustomer?> GetByAirwallexId(string id, CancellationToken ct) =>
    Task.FromResult(this.ByUser.Values.FirstOrDefault(c => c.AirwallexCustomerId == id));

  public Task<PaymentCustomer> Create(string userId, string awx, CancellationToken ct) =>
    Task.FromResult(this.ByUser.GetOrAdd(userId,
      _ => new PaymentCustomer(Guid.NewGuid(), userId, awx, new Dictionary<ConsentPurpose, StoredConsent>(), DateTime.UtcNow, DateTime.UtcNow)));

  public Task<PaymentCustomer?> UpsertConsent(string awx, string? consentId, ConsentStatus? status, ConsentPurpose purpose, CancellationToken ct)
  {
    var c = this.ByUser.Values.FirstOrDefault(x => x.AirwallexCustomerId == awx);
    if (c is null) return Task.FromResult<PaymentCustomer?>(null);
    var consents = c.Consents.ToDictionary();
    if (consentId is null) consents.Remove(purpose);
    else consents[purpose] = new StoredConsent(consentId, status);
    var next = c with { Consents = consents };
    this.ByUser[c.UserId] = next;
    return Task.FromResult<PaymentCustomer?>(next);
  }

  public Task<bool> DeleteConsent(Owner owner, ConsentPurpose purpose, string expected, CancellationToken ct)
  {
    if (!this.ByUser.TryGetValue(owner.UserId!, out var c) || c.ConsentFor(purpose)?.ConsentId != expected) return Task.FromResult(false);
    var consents = c.Consents.ToDictionary();
    consents.Remove(purpose);
    this.ByUser[c.UserId] = c with { Consents = consents };
    return Task.FromResult(true);
  }
}

internal sealed class MemCharges : IChargeRepository
{
  public ConcurrentDictionary<Guid, ChargeRecord> Rows { get; } = new();

  public Task<ChargeRecord> EnsureLocked(ChargeRecord fresh, CancellationToken ct) =>
    Task.FromResult(Copy(this.Rows.GetOrAdd(fresh.RequestId, fresh)));

  public Task<ChargeRecord?> Lock(Guid requestId, CancellationToken ct) => this.Get(requestId, ct);

  public Task<ChargeRecord?> LockByIntent(string intentId, CancellationToken ct) =>
    Task.FromResult(this.Rows.Values.Where(r => r.IntentId == intentId).Select(Copy).FirstOrDefault());

  public Task<ChargeRecord?> Get(Guid requestId, CancellationToken ct) =>
    Task.FromResult(this.Rows.TryGetValue(requestId, out var r) ? Copy(r) : null);

  public Task Save(ChargeRecord row, CancellationToken ct)
  {
    this.Rows[row.RequestId] = Copy(row);
    return Task.CompletedTask;
  }

  private static ChargeRecord Copy(ChargeRecord r) => new()
  {
    RequestId = r.RequestId,
    UserId = r.UserId,
    Purpose = r.Purpose,
    AmountCents = r.AmountCents,
    Currency = r.Currency,
    Description = r.Description,
    State = r.State,
    Attempt = r.Attempt,
    IntentId = r.IntentId,
    GatewayStatus = r.GatewayStatus,
    LastError = r.LastError,
    InFlightUntil = r.InFlightUntil,
    CreatedAt = r.CreatedAt,
    UpdatedAt = r.UpdatedAt,
  };
}

internal sealed class Rig
{
  public MemCustomers Customers { get; } = new();
  public MemCharges Charges { get; } = new();
  public FakeAirwallex Gateway { get; } = new();
  public ListOutbox Outbox { get; } = new();

  public ChargeService Service() =>
    new(this.Charges, this.Customers, this.Gateway, new InlineUow(), this.Outbox, TimeProvider.System, NullLogger<ChargeService>.Instance);

  public Task<ChargeResult> Charge(Guid id, string user = "u1", long cents = 500) =>
    this.Service().Charge(id, user, cents, "usd", ConsentPurpose.Penalty, null, CancellationToken.None);

  public IEnumerable<string> Ops(string op) => this.Gateway.Calls.Where(c => c.StartsWith(op + ":", StringComparison.Ordinal));
}
