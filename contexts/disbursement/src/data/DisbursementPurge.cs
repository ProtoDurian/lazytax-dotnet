using LazyTax.Platform.Lifecycle;

namespace Ctx.Disbursement.Data;

/// <summary>
/// Payout rows carry no user id (they are per charity and currency), so there is nothing of the user's to delete.
/// Registered anyway so the account purge covers every context, and so a future user-keyed column cannot be forgotten.
/// </summary>
public sealed class DisbursementPurge : IUserPurge
{
  public Task Purge(string userId, CancellationToken ct) => Task.CompletedTask;
}
