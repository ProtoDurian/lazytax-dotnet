using System.Collections.Concurrent;
using Ctx.Payment.Contract.V1;
using Ctx.Payment.Domain;

namespace Ctx.Payment.Data;

/// <summary>
/// The Airwallex fake: in-memory, no network, one per app (singleton). Behaves like the real gateway where
/// payment depends on it: a repeated request id returns the same intent, an unknown intent is a not-found,
/// confirm moves an intent to SUCCEEDED. Tests script failures with <see cref="FailNext"/> and outcomes with
/// <see cref="NextConfirmStatus"/>.
/// </summary>
public sealed class FakeAirwallex : IPaymentGateway
{
  private readonly ConcurrentDictionary<string, string> customersByMerchant = new();
  private readonly ConcurrentDictionary<string, GatewayIntent> intents = new();
  private readonly ConcurrentDictionary<string, string> intentByRequest = new();
  private readonly ConcurrentQueue<(string Op, bool Definitive)> failures = new();
  private readonly ConcurrentQueue<string> confirmStatuses = new();
  private readonly ConcurrentQueue<string> calls = new();

  /// <summary>Every call, e.g. <c>create:{requestId}</c>, <c>confirm:{intentId}</c>, <c>disable:{consentId}</c>.</summary>
  public IReadOnlyList<string> Calls => [.. this.calls];

  public IReadOnlySet<string> DisabledConsents => this.disabled.Keys.ToHashSet();

  private readonly ConcurrentDictionary<string, bool> disabled = new();

  /// <summary>The next call of <paramref name="op"/> (create, confirm, retrieve, disable, secret, customer) throws.</summary>
  public void FailNext(string op, bool definitive = false) => this.failures.Enqueue((op, definitive));

  /// <summary>The next confirm leaves the intent in <paramref name="status"/> instead of SUCCEEDED.</summary>
  public void NextConfirmStatus(string status) => this.confirmStatuses.Enqueue(status);

  public Task<string?> FindCustomer(string merchantCustomerId, CancellationToken ct)
  {
    this.Call("customer", merchantCustomerId);
    return Task.FromResult(this.customersByMerchant.GetValueOrDefault(merchantCustomerId));
  }

  public Task<string> CreateCustomer(string merchantCustomerId, CancellationToken ct)
  {
    this.Call("customer", merchantCustomerId);
    return Task.FromResult(this.customersByMerchant.GetOrAdd(merchantCustomerId, _ => "cus_" + Guid.NewGuid().ToString("N")[..16]));
  }

  public Task<string> ClientSecret(string customerId, CancellationToken ct)
  {
    this.Call("secret", customerId);
    return Task.FromResult("cs_" + Guid.NewGuid().ToString("N"));
  }

  public Task DisableConsent(string consentId, CancellationToken ct)
  {
    this.Call("disable", consentId);
    this.disabled[consentId] = true;
    return Task.CompletedTask;
  }

  public Task<GatewayIntent> CreateIntent(IntentRequest req, CancellationToken ct)
  {
    this.Call("create", req.RequestId);
    var id = this.intentByRequest.GetOrAdd(req.RequestId, _ =>
    {
      var intent = new GatewayIntent("int_" + Guid.NewGuid().ToString("N")[..16], "REQUIRES_PAYMENT_METHOD",
        req.Amount, req.Currency, req.CustomerId, req.MerchantOrderId);
      this.intents[intent.Id] = intent;
      return intent.Id;
    });
    return Task.FromResult(this.intents[id]);
  }

  public Task<GatewayIntent> RetrieveIntent(string intentId, CancellationToken ct)
  {
    this.Call("retrieve", intentId);
    return this.intents.TryGetValue(intentId, out var i)
      ? Task.FromResult(i)
      : throw new GatewayException($"payment intent {intentId} not found", definitive: true, notFound: true);
  }

  public Task<GatewayIntent> ConfirmIntent(string intentId, string consentId, string customerId, CancellationToken ct)
  {
    this.Call("confirm", intentId);
    if (!this.intents.TryGetValue(intentId, out var i))
      throw new GatewayException($"payment intent {intentId} not found", definitive: true, notFound: true);
    if (i.CustomerId != customerId) throw new GatewayException("intent belongs to another customer", definitive: true);
    if (this.disabled.ContainsKey(consentId)) throw new GatewayException("payment consent is disabled", definitive: true);
    if (i.Succeeded) return Task.FromResult(i); // confirm of a settled intent moves no money again
    var status = this.confirmStatuses.TryDequeue(out var s) ? s : "SUCCEEDED";
    var next = i with { Status = status };
    this.intents[intentId] = next;
    return Task.FromResult(next);
  }

  /// <summary>Test hook: settle an intent at the "gateway" (as if the customer finished an action).</summary>
  public void Settle(string intentId) =>
    this.intents.AddOrUpdate(intentId, _ => throw new KeyNotFoundException(intentId), (_, i) => i with { Status = "SUCCEEDED" });

  public int ChargesMoved(string customerId) => this.intents.Values.Count(i => i.CustomerId == customerId && i.Succeeded);

  private void Call(string op, string arg)
  {
    this.calls.Enqueue($"{op}:{arg}");
    if (this.failures.TryPeek(out var f) && f.Op == op && this.failures.TryDequeue(out f))
      throw new GatewayException($"fake airwallex: {op} failed", f.Definitive);
  }
}

/// <summary>Logto management fake: records the claims it would set (no network).</summary>
public sealed class FakeIdentityClaims : IIdentityClaims
{
  private readonly ConcurrentDictionary<(string User, string Claim), string> values = new();

  public string? Get(string userId, string claim) => this.values.GetValueOrDefault((userId, claim));

  public Task SetClaim(string userId, string claim, string value, CancellationToken ct)
  {
    this.values[(userId, claim)] = value;
    return Task.CompletedTask;
  }
}
