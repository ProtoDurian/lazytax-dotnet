namespace LazyTax.Platform.Workers;

/// <summary>
/// A background job of a context, registered with <c>c.Worker&lt;T&gt;()</c>. The kernel calls
/// <see cref="Tick"/> every <see cref="Interval"/> in a fresh DI scope (so a fresh shared connection),
/// logs and swallows failures, and never crashes the API. Make ticks idempotent so replicas are safe;
/// wrap writes in <c>IUnitOfWork.Start</c>.
/// </summary>
public interface IContextWorker
{
  TimeSpan Interval { get; }

  Task Tick(CancellationToken ct);
}
