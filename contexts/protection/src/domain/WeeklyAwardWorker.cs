using LazyTax.Platform.Workers;
using Microsoft.Extensions.Logging;

namespace Ctx.Protection.Domain;

/// <summary>Hourly: finishes the week that just ended for users who settled days lately (LazyTax WeeklyFreezeAwardHostedService).</summary>
public sealed class WeeklyAwardWorker(ProtectionService service, ILogger<WeeklyAwardWorker> log) : IContextWorker
{
  public TimeSpan Interval => TimeSpan.FromHours(1);

  public async Task Tick(CancellationToken ct)
  {
    var n = await service.AwardWeeklyForRecentUsers(DateTime.UtcNow, ct,
      (userId, e) => log.LogWarning(e, "weekly award failed for UserId={UserId}", userId));
    if (n > 0) log.LogInformation("weekly award: {Count} freezes awarded", n);
  }
}
