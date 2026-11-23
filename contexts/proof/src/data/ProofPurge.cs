using LazyTax.Platform.Lifecycle;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Proof.Data;

/// <summary>Account deletion, inside account's one unit of work: bindings, NFC tags, the Strava link and its queued events.</summary>
public sealed class ProofPurge(ProofDb db) : IUserPurge
{
  public async Task Purge(string userId, CancellationToken ct)
  {
    var athletes = await db.StravaConnections.Where(x => x.UserId == userId).Select(x => x.AthleteId).ToListAsync(ct);
    await db.StravaEvents.Where(x => athletes.Contains(x.OwnerId)).ExecuteDeleteAsync(ct);
    await db.StravaConnections.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
    await db.NfcTags.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
    await db.ProofBindings.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
  }
}
