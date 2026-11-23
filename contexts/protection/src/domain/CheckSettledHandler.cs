using LazyTax.Platform.Outbox;

namespace Ctx.Protection.Domain;

/// <summary>The fields of <c>check.settled</c> protection reads (its own record; no reference to check).</summary>
public sealed record CheckSettled(string UserId, Guid HabitId, DateOnly Date, string Status, int? MaxStreak);

/// <summary>Records every settled day for the weekly award and remembers the user's max streak.</summary>
public sealed class CheckSettledHandler(ProtectionService service) : IOutboxSubscriber<CheckSettled>
{
  public Task Handle(OutboxEvent meta, CheckSettled e, CancellationToken ct) =>
    service.RecordSettled(e.UserId, e.HabitId, e.Date, e.Status, e.MaxStreak, ct);
}
