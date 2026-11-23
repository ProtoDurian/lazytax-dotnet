using Ctx.Check.Domain;
using LazyTax.Platform.Lifecycle;
using LazyTax.Platform.Outbox;
using LazyTax.Platform.Workers;
using Microsoft.Extensions.Logging;

namespace Ctx.Check;

/// <summary>check's own record of habit.changed / habit.deleted (the fields it reads).</summary>
public sealed record HabitIdsChanged(string UserId, Guid[] HabitIds);

/// <summary>check's own record of account.timezone_changed.</summary>
public sealed record TimezoneMoved(string UserId, string? From, string To);

/// <summary>habit.changed: days, deadline or enabled may have changed, so retime the pending checks (LazyTax called RetimePending in HabitService.Update).</summary>
public sealed class HabitChangedHandler(CheckService checks) : IOutboxSubscriber<HabitIdsChanged>
{
  public Task Handle(OutboxEvent meta, HabitIdsChanged e, CancellationToken ct) => checks.RetimePending(e.UserId, e.HabitIds, null, ct);
}

/// <summary>habit.deleted: drop the habits' pending checks and hide their executions.</summary>
public sealed class HabitDeletedHandler(ICheckStore store) : IOutboxSubscriber<HabitIdsChanged>
{
  public Task Handle(OutboxEvent meta, HabitIdsChanged e, CancellationToken ct) => store.MarkHabitsDeleted(e.UserId, e.HabitIds, ct);
}

/// <summary>account.timezone_changed: move pending checks to the new timezone (LazyTax called MoveToTimezone synchronously).</summary>
public sealed class TimezoneChangedHandler(CheckService checks) : IOutboxSubscriber<TimezoneMoved>
{
  public async Task Handle(OutboxEvent meta, TimezoneMoved e, CancellationToken ct) => await checks.MoveToTimezone(e.UserId, null, ct);
}

/// <summary>Every 5 minutes: open upcoming checks and settle the due ones (LazyTax CheckSchedulerHostedService). Idempotent across replicas.</summary>
public sealed class CheckTickWorker(CheckService checks, ILogger<CheckTickWorker> logger) : IContextWorker
{
  public TimeSpan Interval => TimeSpan.FromMinutes(5);

  public async Task Tick(CancellationToken ct)
  {
    var n = await checks.Tick(DateTime.UtcNow, ct);
    logger.LogDebug("check tick handled {Count}", n);
  }
}

/// <summary>Account deletion: every check, evidence and execution row of the user.</summary>
public sealed class CheckPurge(ICheckStore store) : IUserPurge
{
  public Task Purge(string userId, CancellationToken ct) => store.Purge(userId, ct);
}
