using Ctx.Charity.Contract.V1;

namespace Ctx.Charity.Domain;

public interface ICharityRepository
{
  Task<CharityModel?> Get(Guid id, CancellationToken ct);
  Task<bool> Exists(Guid id, CancellationToken ct);
  Task<IReadOnlyList<CharityModel>> Search(CharitySearchQuery q, CancellationToken ct);
  Task<CharityModel> Create(CharityRecord record, CancellationToken ct);
  Task<CharityModel?> Update(Guid id, CharityRecord record, CancellationToken ct);
  Task<bool> Delete(Guid id, CancellationToken ct);
  Task<bool> SetCauses(Guid id, IReadOnlyCollection<string> causeKeys, CancellationToken ct);
  Task<bool> AddCause(Guid id, string causeKey, CancellationToken ct);
  Task<CharityModel?> GetByExternalId(string source, string externalKey, CancellationToken ct);
  Task<string?> GetExternalKey(Guid charityId, string source, CancellationToken ct);
  Task<bool> UpsertExternalId(Guid charityId, ExternalIdRecord external, CancellationToken ct);
  Task<BulkUpsertResult> BulkUpsert(IReadOnlyList<BulkUpsertCharity> items, CancellationToken ct);
}

public interface ICauseRepository
{
  Task<CauseModel?> Get(Guid id, CancellationToken ct);
  Task<IReadOnlyList<CauseModel>> Search(CauseSearch search, CancellationToken ct);
  Task<CauseModel> Create(CauseRecord record, CancellationToken ct);
  Task<CauseModel?> Update(Guid id, CauseRecord record, CancellationToken ct);
  Task<bool> Delete(Guid id, CancellationToken ct);
  Task<CauseModel?> GetByKey(string key, CancellationToken ct);
}

/// <summary>Pledge's catalogue (causes and organisations). The only implementation here is a fake with no network.</summary>
public interface IPledgeGateway
{
  Task<IReadOnlyList<PledgeCause>> GetCauses(CancellationToken ct);
  Task<IReadOnlyList<PledgeOrganization>> GetOrganizations(string country, string causeKey, int perPage, CancellationToken ct);
}

public sealed record PledgeCause(int Id, string Name, int? ParentId);

public sealed record PledgeOrganization(
  string Id, string Name, string NgoId, string Mission, string Country, string WebsiteUrl, string ProfileUrl, string LogoUrl);
