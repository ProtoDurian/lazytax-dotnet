using Ctx.Payment.Contract.V1;
using Ctx.Payment.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Lifecycle;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Payment.Data;

public sealed class CustomerRepository(PaymentDb db, TimeProvider clock) : ICustomerRepository
{
  public async Task<PaymentCustomer?> Get(Owner owner, CancellationToken ct)
  {
    if (owner.IsAnyone) throw new ArgumentException("a payment customer read needs a user owner", nameof(owner));
    var row = await db.PaymentCustomers.AsNoTracking().Include(x => x.Consents)
      .OwnedBy(owner, x => x.UserId).FirstOrDefaultAsync(ct);
    return row?.ToDomain();
  }

  public async Task<PaymentCustomer?> GetByAirwallexId(string airwallexCustomerId, CancellationToken ct) =>
    (await db.PaymentCustomers.AsNoTracking().Include(x => x.Consents)
      .FirstOrDefaultAsync(x => x.AirwallexCustomerId == airwallexCustomerId, ct))?.ToDomain();

  public async Task<PaymentCustomer> Create(string userId, string airwallexCustomerId, CancellationToken ct)
  {
    var now = clock.GetUtcNow().UtcDateTime;
    // Unique on UserId: a concurrent create of the same user collapses onto one row.
    await db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO payment."PaymentCustomers" ("Id", "UserId", "AirwallexCustomerId", "CreatedAt", "UpdatedAt")
      VALUES ({Guid.NewGuid()}, {userId}, {airwallexCustomerId}, {now}, {now})
      ON CONFLICT ("UserId") DO NOTHING
      """, ct);
    return await this.Get(Owner.User(userId), ct) ?? throw new InvalidOperationException($"customer of {userId} missing after insert");
  }

  public async Task<PaymentCustomer?> UpsertConsent(
    string airwallexCustomerId, string? consentId, ConsentStatus? status, ConsentPurpose purpose, CancellationToken ct)
  {
    var row = await db.PaymentCustomers.Include(x => x.Consents)
      .FirstOrDefaultAsync(x => x.AirwallexCustomerId == airwallexCustomerId, ct);
    if (row is null) return null;
    var now = clock.GetUtcNow().UtcDateTime;
    var consent = row.Consents.FirstOrDefault(c => c.Purpose == (int)purpose);
    if (consentId is null)
    {
      if (consent is not null) db.PaymentConsents.Remove(consent);
    }
    else if (consent is null)
    {
      db.PaymentConsents.Add(new PaymentConsentRow
      {
        Id = Guid.NewGuid(),
        PaymentCustomerId = row.Id,
        Purpose = (int)purpose,
        ConsentId = consentId,
        Status = ToDb(status),
        CreatedAt = now,
        UpdatedAt = now,
      });
    }
    else
    {
      consent.ConsentId = consentId;
      consent.Status = ToDb(status);
      consent.UpdatedAt = now;
    }
    row.UpdatedAt = now;
    await db.SaveChangesAsync(ct);
    db.ChangeTracker.Clear();
    return await this.GetByAirwallexId(airwallexCustomerId, ct);
  }

  public async Task<bool> DeleteConsent(Owner owner, ConsentPurpose purpose, string expectedConsentId, CancellationToken ct)
  {
    var deleted = await db.PaymentConsents
      .OwnedBy(owner, c => c.PaymentCustomer!.UserId)
      .Where(c => c.Purpose == (int)purpose && c.ConsentId == expectedConsentId)
      .ExecuteDeleteAsync(ct);
    var now = clock.GetUtcNow().UtcDateTime;
    await db.PaymentCustomers.OwnedBy(owner, x => x.UserId)
      .ExecuteUpdateAsync(s => s.SetProperty(p => p.UpdatedAt, now), ct);
    return deleted > 0;
  }

  internal static string? ToDb(ConsentStatus? s) => s switch
  {
    ConsentStatus.Verified => "VERIFIED",
    ConsentStatus.RequiresPaymentMethod => "REQUIRES_PAYMENT_METHOD",
    ConsentStatus.RequiresCustomerAction => "REQUIRES_CUSTOMER_ACTION",
    _ => null,
  };
}

public sealed class ChargeRepository(PaymentDb db) : IChargeRepository
{
  public async Task<ChargeRecord> EnsureLocked(ChargeRecord fresh, CancellationToken ct)
  {
    await db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO payment."Charges" ("Id", "UserId", "Purpose", "AmountCents", "Currency", "Description", "State",
        "Attempt", "CreatedAt", "UpdatedAt")
      VALUES ({fresh.RequestId}, {fresh.UserId}, {(int)fresh.Purpose}, {fresh.AmountCents}, {fresh.Currency},
        {fresh.Description}, {fresh.State.ToString()}, 0, {fresh.CreatedAt}, {fresh.UpdatedAt})
      ON CONFLICT ("Id") DO NOTHING
      """, ct);
    return await this.Lock(fresh.RequestId, ct) ?? throw new InvalidOperationException($"charge {fresh.RequestId} missing after insert");
  }

  public async Task<ChargeRecord?> Lock(Guid requestId, CancellationToken ct) =>
    (await db.Charges.FromSqlInterpolated($"""SELECT * FROM payment."Charges" WHERE "Id" = {requestId} FOR UPDATE""")
      .AsNoTracking().FirstOrDefaultAsync(ct))?.ToDomain();

  public async Task<ChargeRecord?> LockByIntent(string intentId, CancellationToken ct) =>
    (await db.Charges.FromSqlInterpolated($"""SELECT * FROM payment."Charges" WHERE "IntentId" = {intentId} FOR UPDATE""")
      .AsNoTracking().FirstOrDefaultAsync(ct))?.ToDomain();

  public async Task<ChargeRecord?> Get(Guid requestId, CancellationToken ct) =>
    (await db.Charges.AsNoTracking().FirstOrDefaultAsync(x => x.Id == requestId, ct))?.ToDomain();

  public Task Save(ChargeRecord r, CancellationToken ct) =>
    db.Charges.Where(x => x.Id == r.RequestId).ExecuteUpdateAsync(s => s
      .SetProperty(x => x.State, r.State)
      .SetProperty(x => x.Attempt, r.Attempt)
      .SetProperty(x => x.IntentId, r.IntentId)
      .SetProperty(x => x.GatewayStatus, r.GatewayStatus)
      .SetProperty(x => x.LastError, r.LastError == null ? null : r.LastError.Length > 500 ? r.LastError[..500] : r.LastError)
      .SetProperty(x => x.InFlightUntil, r.InFlightUntil)
      .SetProperty(x => x.UpdatedAt, r.UpdatedAt), ct);
}

/// <summary>Account deletion: every payment row of the user (LazyTax DeleteAllRemnants' payment part).</summary>
public sealed class PaymentPurge(PaymentDb db) : IUserPurge
{
  public async Task Purge(string userId, CancellationToken ct)
  {
    await db.Charges.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
    await db.PaymentConsents.Where(x => x.PaymentCustomer!.UserId == userId).ExecuteDeleteAsync(ct);
    await db.PaymentCustomers.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
  }
}

/// <summary>Before the purge, outside the transaction: revoke the mandates at the gateway (best effort).</summary>
public sealed class RevokeConsentsBeforePurge(PaymentService payments) : IBeforePurge
{
  public async Task Run(string userId, CancellationToken ct) => await payments.RevokeAll(userId, ct);
}

internal static class RowMapping
{
  public static PaymentCustomer ToDomain(this PaymentCustomerRow r) => new(
    r.Id, r.UserId, r.AirwallexCustomerId,
    r.Consents.ToDictionary(c => (ConsentPurpose)c.Purpose, c => new StoredConsent(c.ConsentId, Purposes.ParseStatus(c.Status))),
    r.CreatedAt, r.UpdatedAt);

  public static ChargeRecord ToDomain(this ChargeRow r) => new()
  {
    RequestId = r.Id,
    UserId = r.UserId,
    Purpose = (ConsentPurpose)r.Purpose,
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
