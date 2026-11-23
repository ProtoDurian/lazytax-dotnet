using LazyTax.Platform.Data;

namespace Ctx.Account.Domain;

/// <summary>Users, owner-scoped: every read takes an <see cref="Owner"/>.</summary>
public interface IUserRepository
{
  Task<IReadOnlyList<UserPrincipal>> Search(UserSearch search, CancellationToken ct);
  Task<UserPrincipal?> Get(Owner owner, string id, CancellationToken ct);
  Task<UserPrincipal?> GetByUsername(Owner owner, string username, CancellationToken ct);
  Task<UserPrincipal> Create(string id, UserRecord record, CancellationToken ct);
  Task<UserPrincipal?> Update(Owner owner, string id, UserRecord record, CancellationToken ct);
  Task<bool> Exists(string id, CancellationToken ct);
}

/// <summary>Configurations, owner-scoped.</summary>
public interface IConfigurationRepository
{
  Task<ConfigurationPrincipal?> Get(Owner owner, Guid id, CancellationToken ct);
  Task<ConfigurationPrincipal?> GetByUser(Owner owner, CancellationToken ct);
  Task<ConfigurationPrincipal> Create(string userId, ConfigurationRecord record, CancellationToken ct);
  Task<ConfigurationPrincipal?> Update(Owner owner, Guid id, ConfigurationRecord record, CancellationToken ct);
}
