namespace LazyTax.Platform.Outbox;

/// <summary>
/// Transactional outbox (LazyTax's <c>CheckEvents</c>). <see cref="Add"/> writes a row in the CURRENT unit of
/// work, so the event exists only if the transaction commits. After commit, the kernel's dispatcher delivers
/// it to every subscriber, records each delivery, retries with backoff and caps attempts.
/// </summary>
public interface IOutbox
{
  /// <summary>
  /// <paramref name="eventName"/> is <c>"&lt;your context&gt;.&lt;event&gt;"</c>, e.g. <c>"check.settled"</c>.
  /// <paramref name="payload"/> is serialized as camelCase JSON; keep it small (ids, amounts), never emails or tokens.
  /// Must run inside <c>IUnitOfWork.Start</c>.
  /// </summary>
  Task Add(string eventName, object payload, CancellationToken ct = default);
}

/// <summary>
/// Handles one event type. Runs once per event per subscriber (deliveries are recorded), inside its own unit
/// of work. Must be idempotent and must not call HTTP; throw to retry later.
/// </summary>
public interface IOutboxSubscriber<in TPayload> where TPayload : class
{
  Task Handle(OutboxEvent meta, TPayload payload, CancellationToken ct);
}

/// <summary>The event envelope a subscriber receives with the payload.</summary>
public sealed record OutboxEvent(Guid Id, string Name, DateTime CreatedAtUtc, int Attempt);
