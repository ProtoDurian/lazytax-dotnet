using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Ctx.Charity.Domain;

/// <summary>
/// Pulls causes and top organisations per country and cause from Pledge into the catalogue (port of LazyTax's
/// PledgeSyncService). Gateway calls happen OUTSIDE any transaction; each batch upsert is its own unit of work.
/// </summary>
public sealed partial class PledgeSyncService(
  IPledgeGateway gateway, CharityService charities, CauseService causes, ILogger<PledgeSyncService> logger)
{
  public static readonly string[] SupportedCountries =
  [
    "US", "GB", "CA", "AU", "DE", "FR", "NL", "SE", "NO", "DK", "FI", "CH", "AT", "BE", "IE", "NZ", "SG", "HK", "JP", "KR",
    "IN", "ID", "CN", "BR", "MX", "AR", "CL", "CO", "PE", "ES", "IT", "PT", "PL", "CZ", "GR", "TR", "RU", "ZA", "EG", "NG",
    "KE", "TH", "MY", "PH", "VN", "AE", "SA", "IL", "UA", "RO", "HU", "BG", "HR", "SI", "SK", "LT", "LV", "EE", "IS", "LU",
  ];

  private const int PageSize = 25;

  public static string HierarchicalName(PledgeCause cause, IReadOnlyDictionary<int, PledgeCause> lookup)
  {
    var names = new List<string>();
    var visited = new HashSet<int>();
    PledgeCause? current = cause;
    while (current is not null && visited.Add(current.Id))
    {
      names.Add(current.Name);
      current = current.ParentId is { } p && lookup.TryGetValue(p, out var parent) ? parent : null;
    }
    names.Reverse();
    return string.Join(" > ", names);
  }

  public static string Slug(string name)
  {
    if (string.IsNullOrWhiteSpace(name)) return string.Empty;
    var s = name.ToLowerInvariant().Replace(' ', '-');
    s = NotSlugChars().Replace(s, "");
    s = Hyphens().Replace(s, "-");
    return s.Trim('-');
  }

  public async Task<PledgeSyncSummary> Sync(CancellationToken ct)
  {
    var pledgeCauses = await gateway.GetCauses(ct);
    var lookup = pledgeCauses.ToDictionary(c => c.Id);
    var causesUpserted = 0;
    foreach (var c in pledgeCauses)
    {
      var key = c.Id.ToString();
      var name = HierarchicalName(c, lookup);
      var existing = await causes.GetByKey(key, ct);
      if (existing is null)
      {
        await causes.Create(new CauseRecord(key, name), ct);
        causesUpserted++;
      }
      else if (!string.Equals(existing.Record.Name, name, StringComparison.Ordinal))
      {
        await causes.Update(existing.Id, new CauseRecord(existing.Record.Key, name), ct);
        causesUpserted++;
      }
    }

    int created = 0, updated = 0, linked = 0, processed = 0;
    foreach (var country in SupportedCountries)
      foreach (var cause in pledgeCauses)
      {
        var (c, u, l, p) = await Batch(country, cause.Id.ToString(), ct);
        created += c; updated += u; linked += l; processed += p;
      }
    return new PledgeSyncSummary(causesUpserted, created, updated, linked, processed);
  }

  private async Task<(int, int, int, int)> Batch(string country, string causeKey, CancellationToken ct)
  {
    try
    {
      var orgs = await gateway.GetOrganizations(country, causeKey, PageSize, ct);
      if (orgs.Count == 0) return (0, 0, 0, 0);
      var items = orgs.Select(o => new BulkUpsertCharity(
        new CharityRecord(
          o.Name,
          Slug(o.Name),
          string.IsNullOrWhiteSpace(o.Mission) ? null : o.Mission.Length > 8192 ? o.Mission[..8192] : o.Mission,
          string.IsNullOrWhiteSpace(o.Country) ? [] : [o.Country],
          string.IsNullOrWhiteSpace(o.NgoId) ? null : o.NgoId,
          string.IsNullOrWhiteSpace(o.Country) ? null : o.Country,
          string.IsNullOrWhiteSpace(o.WebsiteUrl) ? null : o.WebsiteUrl,
          string.IsNullOrWhiteSpace(o.LogoUrl) ? null : o.LogoUrl),
        new ExternalIdRecord("pledge", o.Id, string.IsNullOrWhiteSpace(o.ProfileUrl) ? null : o.ProfileUrl, null, DateTimeOffset.UtcNow),
        [causeKey])).ToArray();
      var r = await charities.BulkUpsert(items, ct);
      return (r.CharitiesCreated, r.CharitiesUpdated, r.ExternalIdsLinked, orgs.Count);
    }
    catch (Exception e) when (e is not OperationCanceledException)
    {
      logger.LogError(e, "Failed to process batch for {Country} - {CauseKey}", country, causeKey);
      return (0, 0, 0, 0);
    }
  }

  [GeneratedRegex("[^a-z0-9\\-]")]
  private static partial Regex NotSlugChars();

  [GeneratedRegex("-+")]
  private static partial Regex Hyphens();
}
