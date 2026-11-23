using Ctx.Account.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Lifecycle;
using LazyTax.Platform.Outbox;
using LazyTax.Platform.Problems;
using Microsoft.Extensions.Logging;

namespace Ctx.Account.Domain;

/// <summary>LazyTax's UserService, with account deletion over every context's hooks.</summary>
public sealed class UserService(
  IUserRepository repo,
  IUnitOfWork uow,
  IOutbox outbox,
  IEnumerable<IDeleteGuard> deleteGuards,
  IEnumerable<IBeforePurge> beforePurge,
  IEnumerable<IUserPurge> purges,
  ILogger<UserService> logger)
{
  public Task<IReadOnlyList<UserPrincipal>> Search(UserSearch search, CancellationToken ct) => repo.Search(search, ct);

  public async Task<UserPrincipal> Get(Owner owner, string id, CancellationToken ct) =>
    await repo.Get(owner, id, ct) ?? throw new NotFound("User", id);

  public async Task<UserPrincipal> GetByUsername(Owner owner, string username, CancellationToken ct) =>
    await repo.GetByUsername(owner, username, ct) ?? throw new NotFound("User", username);

  public Task<UserPrincipal> Create(string id, UserRecord record, CancellationToken ct) =>
    uow.Start(async () =>
    {
      var created = await repo.Create(id, record, ct);
      // Welcome email and the Logto claim sync happen after commit, from this event.
      await outbox.Add(AccountEvents.UserCreated, new UserCreated(id), ct);
      return created;
    }, ct);

  public async Task<UserPrincipal> Update(Owner owner, string id, UserRecord record, CancellationToken ct) =>
    await uow.Start(() => repo.Update(owner, id, record, ct), ct) ?? throw new NotFound("User", id);

  /// <summary>
  /// Self-service deletion (LazyTax DeleteAccount): every delete guard (penalty: debt) runs first and may
  /// block; then every before-purge hook (payment: revoke consents) OUTSIDE the transaction, best effort; then
  /// every context's purge in ONE unit of work. Idempotent: a missing user is a success.
  /// </summary>
  public async Task DeleteAccount(string userId, CancellationToken ct)
  {
    if (!await repo.Exists(userId, ct)) return;
    foreach (var g in deleteGuards) await g.Check(userId, ct);
    foreach (var h in beforePurge)
    {
      try
      {
        await h.Run(userId, ct);
      }
      catch (Exception e) when (e is not OperationCanceledException)
      {
        logger.LogWarning(e, "Before-purge hook {Hook} failed; deletion continues", h.GetType().Name);
      }
    }
    await this.Purge(userId, ct);
  }

  /// <summary>Admin delete (LazyTax DeleteAllRemnants): the purge only, no debt gate; 404 when the user is gone.</summary>
  public async Task DeleteAllRemnants(string userId, CancellationToken ct)
  {
    if (!await repo.Exists(userId, ct)) throw new NotFound("User", userId);
    await this.Purge(userId, ct);
  }

  private Task Purge(string userId, CancellationToken ct) =>
    uow.Start(async () =>
    {
      foreach (var p in purges) await p.Purge(userId, ct);
      // Logto identity removal is an HTTP call: it follows this event, after commit.
      await outbox.Add(AccountEvents.UserDeleted, new UserDeleted(userId), ct);
    }, ct);
}
