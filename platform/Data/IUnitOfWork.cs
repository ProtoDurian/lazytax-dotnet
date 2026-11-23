namespace LazyTax.Platform.Data;

/// <summary>Isolation of a unit of work. ReadCommitted is the default.</summary>
public enum UnitIsolation
{
  /// <summary>
  /// Default. Every statement sees what committed before it, so "lock, then count, then write" counts
  /// after the lock is granted.
  /// </summary>
  ReadCommitted,

  /// <summary>
  /// Opt-in (LazyTax's TransactionManager level): one snapshot for the whole unit. <c>LockUser</c> must be
  /// the unit's first statement; if the lock is busy it waits, then re-runs the whole unit with a fresh
  /// snapshot, so the count is never stale.
  /// </summary>
  RepeatableRead,
}

/// <summary>
/// The unit of work (LazyTax's <c>ITransactionManager</c>). <see cref="Start{T}(Func{Task{T}}, CancellationToken)"/> opens a
/// <c>TransactionScope(Required, AsyncFlow)</c> on the request's shared connection; a nested call joins the
/// outer one. Writes of every context inside it commit or roll back together.
/// The outermost call re-runs the whole function on SQLSTATE 40001 / 40P01 (at most 3 times) and after an
/// RR lock wait, so the function must be safe to re-run (no HTTP, email or payment calls inside — use the
/// outbox). Throwing (e.g. a <c>Problem</c>) rolls back.
/// </summary>
public interface IUnitOfWork
{
  /// <summary>A ReadCommitted unit (the default).</summary>
  Task<T> Start<T>(Func<Task<T>> work, CancellationToken ct = default);

  /// <summary>A ReadCommitted unit (the default).</summary>
  Task Start(Func<Task> work, CancellationToken ct = default);

  /// <summary>A unit with the given isolation. A nested call joins the outer unit and its isolation.</summary>
  Task<T> Start<T>(UnitIsolation isolation, Func<Task<T>> work, CancellationToken ct = default) => this.Start(work, ct);

  /// <summary>A unit with the given isolation. A nested call joins the outer unit and its isolation.</summary>
  Task Start(UnitIsolation isolation, Func<Task> work, CancellationToken ct = default) => this.Start(work, ct);

  /// <summary>
  /// Per-user advisory lock held until the transaction ends (LazyTax's <c>UserLock.LockUser</c>). Take it
  /// FIRST in every "count, then write" rule. Must run inside a unit. Waits at most 5 s, then throws
  /// <c>UserBusy</c> (409). Never call HTTP while holding it.
  /// </summary>
  Task LockUser(string userId, CancellationToken ct = default);
}
