using LazyTax.Platform.Lifecycle;

namespace Ctx.Charity;

/// <summary>
/// Charity owns no user rows (the catalogue is global; no table has a UserId), so there is nothing to purge.
/// It is registered anyway so "every context has a purge hook" holds, and a test pins that no column holds a user id.
/// </summary>
public sealed class CharityPurge : IUserPurge
{
  public Task Purge(string userId, CancellationToken ct) => Task.CompletedTask;
}
