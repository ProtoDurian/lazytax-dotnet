using System.Transactions;
using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using IsolationLevel = System.Transactions.IsolationLevel;

namespace LazyTax.Kernel.Data;

/// <summary>The DbContext types of every context, so a retry can clear their change trackers.</summary>
public sealed record DbRegistry(IReadOnlyList<Type> Types);

/// <summary>Thrown inside an RR unit after waiting for a busy user lock: the unit re-runs with a fresh snapshot.</summary>
public sealed class LockWaitedRetry() : Exception("The user lock was busy; the unit re-runs with a fresh snapshot");

/// <summary>
/// LazyTax's TransactionManager on the scope's one shared connection: TransactionScope(Required, AsyncFlow),
/// ReadCommitted by default or RepeatableRead on request, a re-run on 40001 / 40P01 (at most 3), and the user lock.
/// </summary>
public sealed class UnitOfWork(SharedConnection shared, IServiceProvider sp, DbRegistry dbs, ILogger<UnitOfWork> logger)
  : IUnitOfWork
{
  public const int MaxRetries = 3;

  /// <summary>RR lock waits re-run too; they make progress (the lock frees), so they get their own cap.</summary>
  public const int MaxLockReruns = 20;

  private UnitIsolation current = UnitIsolation.ReadCommitted;

  public Task<T> Start<T>(Func<Task<T>> work, CancellationToken ct = default) => this.Start(UnitIsolation.ReadCommitted, work, ct);

  public Task Start(Func<Task> work, CancellationToken ct = default) => this.Start(UnitIsolation.ReadCommitted, work, ct);

  public Task Start(UnitIsolation isolation, Func<Task> work, CancellationToken ct = default) =>
    this.Start(isolation, async () =>
    {
      await work();
      return true;
    }, ct);

  public async Task<T> Start<T>(UnitIsolation isolation, Func<Task<T>> work, CancellationToken ct = default)
  {
    // Join an outer unit of work (TransactionScopeOption.Required): one transaction, one connection.
    if (Transaction.Current is not null) return await work();

    var retries = 0;
    var lockReruns = 0;
    while (true)
    {
      var conn = shared.Connection;
      if (conn.State != System.Data.ConnectionState.Closed) await conn.CloseAsync();
      this.current = isolation;
      try
      {
        T result;
        using (var scope = new TransactionScope(TransactionScopeOption.Required,
                 new TransactionOptions
                 {
                   IsolationLevel = isolation == UnitIsolation.RepeatableRead ? IsolationLevel.RepeatableRead : IsolationLevel.ReadCommitted,
                 },
                 TransactionScopeAsyncFlowOption.Enabled))
        {
          // Opened inside the scope, so it enlists. Every DbContext of the scope uses this connection.
          await conn.OpenAsync(ct);
          result = await work();
          scope.Complete();
        }
        return result;
      }
      catch (Exception e) when (Find<LockWaitedRetry>(e) is not null && lockReruns < MaxLockReruns)
      {
        lockReruns++;
        this.ClearTrackers();
      }
      catch (Exception e) when (retries < MaxRetries && IsRetryable(e))
      {
        retries++;
        logger.LogWarning("Unit of work hit {SqlState}; retry {Retry} of {Max}", SqlState(e), retries, MaxRetries);
        this.ClearTrackers();
        await Task.Delay(TimeSpan.FromMilliseconds((20 * retries) + Random.Shared.Next(20)), ct);
      }
      finally
      {
        this.current = UnitIsolation.ReadCommitted;
        await conn.CloseAsync();
      }
    }
  }

  public async Task LockUser(string userId, CancellationToken ct = default)
  {
    if (Transaction.Current is null)
      throw new InvalidOperationException("LockUser must run inside IUnitOfWork.Start");
    var conn = shared.Connection;
    var key = "user:" + userId;
    try
    {
      if (this.current == UnitIsolation.ReadCommitted)
      {
        await SetLockTimeout(conn, ct);
        // ReadCommitted: the next statement takes a new snapshot, after the lock is granted.
        await using var wait = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@k, 0))", conn);
        wait.Parameters.AddWithValue("k", key);
        await wait.ExecuteNonQueryAsync(ct);
        return;
      }

      // RepeatableRead: this statement fixes the unit's snapshot. Granted at once: the snapshot is current.
      await using (var tryLock = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(hashtextextended(@k, 0))", conn))
      {
        tryLock.Parameters.AddWithValue("k", key);
        var granted = await tryLock.ExecuteScalarAsync(ct) is true;
        await SetLockTimeout(conn, ct);
        if (granted) return;
      }

      // Busy: wait until the holder commits, then re-run the whole unit so its snapshot sees that commit.
      await using (var wait = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@k, 0))", conn))
      {
        wait.Parameters.AddWithValue("k", key);
        await wait.ExecuteNonQueryAsync(ct);
      }
      throw new LockWaitedRetry();
    }
    catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.LockNotAvailable)
    {
      throw new UserBusy(userId);
    }
  }

  // Applies to every lock wait for the rest of the transaction (LazyTax UserLock).
  private static async Task SetLockTimeout(NpgsqlConnection conn, CancellationToken ct)
  {
    await using var set = new NpgsqlCommand("SET LOCAL lock_timeout = '5s'", conn);
    await set.ExecuteNonQueryAsync(ct);
  }

  private void ClearTrackers()
  {
    foreach (var t in dbs.Types)
      if (sp.GetService(t) is DbContext db) db.ChangeTracker.Clear();
  }

  public static bool IsRetryable(Exception e) =>
    SqlState(e) is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected;

  private static TEx? Find<TEx>(Exception? e) where TEx : Exception
  {
    for (; e is not null; e = e.InnerException)
      if (e is TEx t) return t;
    return null;
  }

  private static string? SqlState(Exception? e) => Find<PostgresException>(e)?.SqlState;
}
