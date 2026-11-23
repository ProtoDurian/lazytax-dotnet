using Ctx.Disbursement.Contract.V1;
using Ctx.Disbursement.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Disbursement.Data;

/// <summary>
/// Payouts belong to no user, so reads take no <c>Owner</c>; every route is admin-only. The two Mark methods
/// row-lock first, so a reconcile racing the original donate serialises here.
/// </summary>
public sealed class DisbursementRepository(DisbursementDb db) : IDisbursementRepository
{
  public async Task Insert(DisbursementInfo row, CancellationToken ct = default)
  {
    db.Disbursements.Add(ToRow(row));
    await db.SaveChangesAsync(ct);
  }

  public async Task<bool> MarkCompleted(Guid id, string providerDonationId, CancellationToken ct = default)
  {
    var d = await this.Lock(id, ct);
    if (d is null || d.Status == DisbursementStatus.Completed) return false;
    d.Status = DisbursementStatus.Completed;
    d.ProviderDonationId = providerDonationId;
    d.LastError = null;
    d.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync(ct);
    return true;
  }

  public async Task<bool> MarkFailed(Guid id, string error, CancellationToken ct = default)
  {
    var d = await this.Lock(id, ct);
    if (d is null || d.Status == DisbursementStatus.Completed) return false;
    d.Status = DisbursementStatus.Failed;
    d.LastError = error.Length > 2048 ? error[..2048] : error;
    d.Attempts += 1;
    d.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync(ct);
    return true;
  }

  public async Task<IReadOnlyList<DisbursementInfo>> GetReconcilable(DateTime cutoffUtc, int batchSize, CancellationToken ct = default) =>
    (await db.Disbursements.AsNoTracking()
      .Where(d => d.Status == DisbursementStatus.Pending && d.UpdatedAt < cutoffUtc)
      .OrderBy(d => d.UpdatedAt).Take(batchSize).ToListAsync(ct)).Select(ToInfo).ToList();

  public async Task<DisbursementInfo?> Get(Guid id, CancellationToken ct = default) =>
    (await db.Disbursements.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct)) is { } r ? ToInfo(r) : null;

  public async Task<IReadOnlyList<DisbursementInfo>> Search(DisbursementSearch s, CancellationToken ct = default)
  {
    var q = db.Disbursements.AsNoTracking();
    if (s.Status is not null) q = q.Where(d => d.Status == s.Status);
    if (s.CharityId is { } c) q = q.Where(d => d.CharityId == c);
    return (await q.OrderByDescending(d => d.CreatedAt).ThenBy(d => d.Id)
      .Skip(Math.Max(0, s.Skip)).Take(Math.Clamp(s.Limit, 1, 100)).ToListAsync(ct)).Select(ToInfo).ToList();
  }

  // FromSqlRaw + ToListAsync (not FirstOrDefault): EF runs the FOR UPDATE verbatim, without a subquery wrapper.
  private async Task<DisbursementRow?> Lock(Guid id, CancellationToken ct) =>
    (await db.Disbursements
      .FromSqlRaw("SELECT * FROM disbursement.\"Disbursements\" WHERE \"Id\" = {0} FOR UPDATE", id)
      .ToListAsync(ct)).FirstOrDefault();

  private static DisbursementRow ToRow(DisbursementInfo i) => new()
  {
    Id = i.Id,
    CharityId = i.CharityId,
    Currency = i.Currency,
    AmountCents = i.AmountCents,
    Status = i.Status,
    PledgeOrganizationId = i.PledgeOrganizationId,
    ProviderDonationId = i.ProviderDonationId,
    Attempts = i.Attempts,
    LastError = i.LastError,
    CreatedAt = i.CreatedAt,
    UpdatedAt = i.UpdatedAt,
  };

  private static DisbursementInfo ToInfo(DisbursementRow r) => new(r.Id, r.CharityId, r.Currency, r.AmountCents,
    r.Status, r.PledgeOrganizationId, r.ProviderDonationId, r.Attempts, r.LastError, r.CreatedAt, r.UpdatedAt);
}
