using Ctx.Account.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using LazyTax.Platform.Problems;

namespace Ctx.Account.Domain;

/// <summary>LazyTax's ConfigurationService. A timezone change is an outbox event now (check subscribes).</summary>
public sealed class ConfigurationService(IConfigurationRepository repo, IUnitOfWork uow, IOutbox outbox)
{
  public async Task<ConfigurationPrincipal> Get(Owner owner, Guid id, CancellationToken ct) =>
    await repo.Get(owner, id, ct) ?? throw new NotFound("Configuration", id.ToString());

  public async Task<ConfigurationPrincipal> GetByUser(string userId, CancellationToken ct) =>
    await repo.GetByUser(Owner.User(userId), ct) ?? throw new NotFound("Configuration", userId);

  public Task<ConfigurationPrincipal> Create(string userId, ConfigurationRecord record, CancellationToken ct)
  {
    Validate(record);
    return uow.Start(() => repo.Create(userId, record, ct), ct);
  }

  public Task<ConfigurationPrincipal> Update(Owner owner, Guid id, ConfigurationRecord record, CancellationToken ct)
  {
    Validate(record);
    return uow.Start(async () =>
    {
      var before = await repo.Get(owner, id, ct) ?? throw new NotFound("Configuration", id.ToString());
      var updated = await repo.Update(owner, id, record, ct) ?? throw new NotFound("Configuration", id.ToString());
      if (before.Record.Timezone != record.Timezone)
        await outbox.Add(AccountEvents.TimezoneChanged, new TimezoneChanged(updated.UserId, before.Record.Timezone, record.Timezone), ct);
      return updated;
    }, ct);
  }

  private static void Validate(ConfigurationRecord r)
  {
    if (!Timezones.IsValid(r.Timezone))
      throw new LazyTax.Platform.Problems.ValidationFailed("Invalid CreateConfigurationReq", "Timezone", "Timezone must be a valid IANA timezone of 1 to 64 characters");
  }
}
