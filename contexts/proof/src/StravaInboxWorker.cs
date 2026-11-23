using Ctx.Proof.Domain;
using LazyTax.Platform.Workers;

namespace Ctx.Proof;

/// <summary>Drains the Strava webhook inbox: fetches each activity and routes it to the strava plugin. Idle unless Strava is enabled.</summary>
public sealed class StravaInboxWorker(StravaService strava, ProofOptions options) : IContextWorker
{
  private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

  public TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(1, options.Strava.PollSeconds));

  public async Task Tick(CancellationToken ct)
  {
    if (!options.Strava.Enabled) return;
    await strava.DrainInbox(50, Lease, options.Strava.MaxAttempts, ct);
  }
}
