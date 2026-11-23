using Ctx.Charity.Contract.V1;
using Ctx.Charity.Domain;
using LazyTax.Platform.Problems;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Charity.Data;

/// <summary>The charity catalogue is global (no user column), so reads take no <c>Owner</c>.</summary>
public sealed class CharityRepository(CharityDb db) : ICharityRepository
{
  internal static CharityModel ToModel(CharityRow r) => new(r.Id, new CharityRecord(
    r.Name, r.Slug, r.Mission, r.Countries, r.PrimaryRegistrationNumber, r.PrimaryRegistrationCountry, r.WebsiteUrl, r.LogoUrl));

  private static void Apply(CharityRow row, CharityRecord r)
  {
    row.Name = r.Name;
    row.Slug = r.Slug;
    row.Mission = r.Mission;
    row.Countries = r.Countries ?? [];
    row.PrimaryRegistrationNumber = r.PrimaryRegistrationNumber;
    row.PrimaryRegistrationCountry = r.PrimaryRegistrationCountry;
    row.WebsiteUrl = r.WebsiteUrl;
    row.LogoUrl = r.LogoUrl;
  }

  public async Task<CharityModel?> Get(Guid id, CancellationToken ct) =>
    (await db.Charities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)) is { } r ? ToModel(r) : null;

  public Task<bool> Exists(Guid id, CancellationToken ct) => db.Charities.AnyAsync(x => x.Id == id, ct);

  public async Task<IReadOnlyList<CharityModel>> Search(CharitySearchQuery s, CancellationToken ct)
  {
    var q = db.Charities.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(s.Name)) q = q.Where(x => EF.Functions.ILike(x.Name, $"%{s.Name}%"));
    if (!string.IsNullOrWhiteSpace(s.Slug)) q = q.Where(x => x.Slug != null && EF.Functions.ILike(x.Slug, $"%{s.Slug}%"));
    if (!string.IsNullOrWhiteSpace(s.PrimaryRegistrationNumber)) q = q.Where(x => x.PrimaryRegistrationNumber == s.PrimaryRegistrationNumber);
    if (!string.IsNullOrWhiteSpace(s.PrimaryRegistrationCountry)) q = q.Where(x => x.PrimaryRegistrationCountry == s.PrimaryRegistrationCountry);
    if (!string.IsNullOrWhiteSpace(s.Country)) q = q.Where(x => x.Countries.Contains(s.Country));
    if (!string.IsNullOrWhiteSpace(s.CauseKey))
      q = q.Where(c => db.CharityCauses.Any(cc => cc.CharityId == c.Id && db.Causes.Any(k => k.Id == cc.CauseId && k.Key == s.CauseKey)));
    var rows = await q.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip(s.Skip).Take(s.Limit).ToListAsync(ct);
    return [.. rows.Select(ToModel)];
  }

  public async Task<CharityModel> Create(CharityRecord record, CancellationToken ct)
  {
    var row = new CharityRow { Name = record.Name };
    Apply(row, record);
    db.Charities.Add(row);
    await db.SaveChangesAsync(ct);
    return ToModel(row);
  }

  public async Task<CharityModel?> Update(Guid id, CharityRecord record, CancellationToken ct)
  {
    var row = await db.Charities.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (row is null) return null;
    Apply(row, record);
    await db.SaveChangesAsync(ct);
    return ToModel(row);
  }

  public async Task<bool> Delete(Guid id, CancellationToken ct) =>
    await db.Charities.Where(x => x.Id == id).ExecuteDeleteAsync(ct) > 0;

  public async Task<bool> SetCauses(Guid id, IReadOnlyCollection<string> causeKeys, CancellationToken ct)
  {
    if (!await db.Charities.AnyAsync(x => x.Id == id, ct)) return false;
    var keys = causeKeys.Distinct().ToArray();
    var found = await db.Causes.Where(c => keys.Contains(c.Key)).Select(c => new { c.Id, c.Key }).ToListAsync(ct);
    var missing = keys.Except(found.Select(f => f.Key)).ToArray();
    if (missing.Length > 0)
      throw new ValidationFailed("Some causes not found", new Dictionary<string, string[]> { ["causeKeys"] = missing });

    var desired = found.Select(f => f.Id).ToHashSet();
    var existing = await db.CharityCauses.Where(cc => cc.CharityId == id).ToListAsync(ct);
    db.CharityCauses.RemoveRange(existing.Where(e => !desired.Contains(e.CauseId)));
    var have = existing.Select(e => e.CauseId).ToHashSet();
    db.CharityCauses.AddRange(desired.Except(have).Select(cid => new CharityCauseRow { CharityId = id, CauseId = cid }));
    await db.SaveChangesAsync(ct);
    return true;
  }

  public async Task<bool> AddCause(Guid id, string causeKey, CancellationToken ct)
  {
    if (!await db.Charities.AnyAsync(x => x.Id == id, ct)) return false;
    var cause = await db.Causes.FirstOrDefaultAsync(c => c.Key == causeKey, ct)
                ?? throw new ValidationFailed("Cause not found", "causeKey", causeKey);
    if (!await db.CharityCauses.AnyAsync(cc => cc.CharityId == id && cc.CauseId == cause.Id, ct))
    {
      db.CharityCauses.Add(new CharityCauseRow { CharityId = id, CauseId = cause.Id });
      await db.SaveChangesAsync(ct);
    }
    return true;
  }

  public async Task<CharityModel?> GetByExternalId(string source, string externalKey, CancellationToken ct)
  {
    var link = await db.ExternalIds.AsNoTracking().FirstOrDefaultAsync(x => x.Source == source && x.ExternalKey == externalKey, ct);
    if (link is null) return null;
    var charity = await db.Charities.AsNoTracking().FirstOrDefaultAsync(c => c.Id == link.CharityId, ct);
    return charity is null ? null : ToModel(charity);
  }

  public Task<string?> GetExternalKey(Guid charityId, string source, CancellationToken ct) =>
    db.ExternalIds.AsNoTracking().Where(x => x.CharityId == charityId && x.Source == source)
      .OrderByDescending(x => x.LastSyncedAt).ThenBy(x => x.ExternalKey).Select(x => x.ExternalKey).FirstOrDefaultAsync(ct);

  public async Task<bool> UpsertExternalId(Guid charityId, ExternalIdRecord external, CancellationToken ct)
  {
    if (!await db.Charities.AnyAsync(c => c.Id == charityId, ct)) return false;
    var existing = await db.ExternalIds.FirstOrDefaultAsync(x => x.Source == external.Source && x.ExternalKey == external.ExternalKey, ct);
    if (existing is null)
      db.ExternalIds.Add(new ExternalIdRow
      {
        CharityId = charityId,
        Source = external.Source,
        ExternalKey = external.ExternalKey,
        Url = external.Url,
        Payload = external.Payload,
        LastSyncedAt = external.LastSyncedAt,
      });
    else if (existing.CharityId != charityId)
      throw new Conflict("ExternalId is already linked to a different Charity");
    else
    {
      existing.Url = external.Url;
      existing.Payload = external.Payload;
      existing.LastSyncedAt = external.LastSyncedAt;
    }
    await db.SaveChangesAsync(ct);
    return true;
  }

  public async Task<BulkUpsertResult> BulkUpsert(IReadOnlyList<BulkUpsertCharity> items, CancellationToken ct)
  {
    if (items.Count == 0) return new BulkUpsertResult(0, 0, 0, 0);
    var source = items[0].ExternalId.Source;
    if (items.Any(i => i.ExternalId.Source != source))
      throw new ValidationFailed("Invalid bulk upsert", "source", "All items must have the same ExternalId.Source");

    var keys = items.Select(i => i.ExternalId.ExternalKey).Distinct().ToArray();
    var extRows = await db.ExternalIds.Where(e => e.Source == source && keys.Contains(e.ExternalKey)).ToListAsync(ct);
    var charityIds = extRows.Select(e => e.CharityId).Distinct().ToArray();
    var charitiesById = await db.Charities.Where(c => charityIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
    var extByKey = extRows.ToDictionary(e => e.ExternalKey);

    var byKey = new Dictionary<string, CharityRow>();
    int created = 0, updated = 0;
    foreach (var item in items)
    {
      var k = item.ExternalId.ExternalKey;
      if (byKey.TryGetValue(k, out var seen)) { Apply(seen, item.Charity); continue; }
      if (extByKey.TryGetValue(k, out var ext) && charitiesById.TryGetValue(ext.CharityId, out var row))
      {
        Apply(row, item.Charity);
        updated++;
      }
      else
      {
        row = new CharityRow { Name = item.Charity.Name };
        Apply(row, item.Charity);
        db.Charities.Add(row);
        created++;
      }
      byKey[k] = row;
    }
    await db.SaveChangesAsync(ct);

    var linked = 0;
    foreach (var item in items.GroupBy(i => i.ExternalId.ExternalKey).Select(g => g.Last()))
    {
      var k = item.ExternalId.ExternalKey;
      if (extByKey.TryGetValue(k, out var ext))
      {
        ext.Url = item.ExternalId.Url;
        ext.Payload = item.ExternalId.Payload;
        ext.LastSyncedAt = item.ExternalId.LastSyncedAt;
      }
      else
        db.ExternalIds.Add(new ExternalIdRow
        {
          CharityId = byKey[k].Id,
          Source = source,
          ExternalKey = k,
          Url = item.ExternalId.Url,
          Payload = item.ExternalId.Payload,
          LastSyncedAt = item.ExternalId.LastSyncedAt,
        });
      linked++;
    }
    await db.SaveChangesAsync(ct);

    var causeKeys = items.SelectMany(i => i.CauseKeys).Distinct().ToArray();
    var causeIds = await db.Causes.Where(c => causeKeys.Contains(c.Key)).ToDictionaryAsync(c => c.Key, c => c.Id, ct);
    var desired = new HashSet<(Guid, Guid)>();
    foreach (var item in items)
      foreach (var ck in item.CauseKeys)
        if (causeIds.TryGetValue(ck, out var cid)) desired.Add((byKey[item.ExternalId.ExternalKey].Id, cid));
    var ids = byKey.Values.Select(c => c.Id).Distinct().ToArray();
    var have = (await db.CharityCauses.Where(cc => ids.Contains(cc.CharityId)).Select(cc => new { cc.CharityId, cc.CauseId }).ToListAsync(ct))
      .Select(x => (x.CharityId, x.CauseId)).ToHashSet();
    var toAdd = desired.Except(have).ToList();
    db.CharityCauses.AddRange(toAdd.Select(p => new CharityCauseRow { CharityId = p.Item1, CauseId = p.Item2 }));
    await db.SaveChangesAsync(ct);
    return new BulkUpsertResult(created, updated, linked, toAdd.Count);
  }
}
