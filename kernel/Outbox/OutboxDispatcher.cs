using System.Text.Json;
using LazyTax.Platform.Context;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using Npgsql;

namespace LazyTax.Kernel.Outbox;

public sealed class OutboxOptions
{
  public int PollMilliseconds { get; set; } = 2000;
  public int BatchSize { get; set; } = 50;
  public int MaxAttempts { get; set; } = 8;

  /// <summary>Backoff = min(BackoffCapSeconds, BackoffBaseSeconds * 2^attempts).</summary>
  public int BackoffBaseSeconds { get; set; } = 60;
  public int BackoffCapSeconds { get; set; } = 6 * 3600;
  public bool Enabled { get; set; } = true;
}

/// <summary>Delivers due outbox events to every subscriber (LazyTax's DispatchEvents). Tests call <see cref="DispatchOnce"/>.</summary>
public interface IOutboxDispatcher
{
  /// <summary>Delivers what is due now; returns how many events were fully dispatched.</summary>
  Task<int> DispatchOnce(CancellationToken ct = default);
}

public sealed class OutboxDispatcher(
  NpgsqlDataSource dataSource,
  IServiceScopeFactory scopes,
  IReadOnlyList<SubscriptionDefinition> subscriptions,
  OutboxOptions options,
  ILogger<OutboxDispatcher> logger) : IOutboxDispatcher
{
  private sealed record Claimed(Guid Id, string Name, string Payload, DateTime CreatedAt, int Attempts);

  public async Task<int> DispatchOnce(CancellationToken ct = default)
  {
    var events = await this.Claim(ct);
    var done = 0;
    foreach (var ev in events)
    {
      try
      {
        // Each subscriber on its own: one failing does not hold back the others (their deliveries are recorded).
        var errors = new List<Exception>();
        foreach (var sub in subscriptions.Where(s => s.EventName == ev.Name))
        {
          try
          {
            await this.Deliver(ev, sub, ct);
          }
          catch (Exception e) when (e is not OperationCanceledException)
          {
            errors.Add(e);
          }
        }
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(errors[0]);
        if (errors.Count > 1) throw new AggregateException(errors);
        await this.Mark(ev.Id, "UPDATE kernel.outbox_events SET dispatched_at = now(), attempts = attempts + 1 WHERE id = @id", null, ct);
        done++;
      }
      catch (Exception e)
      {
        var seconds = Math.Min(options.BackoffCapSeconds, options.BackoffBaseSeconds * Math.Pow(2, ev.Attempts));
        logger.LogError(e, "Outbox event {EventId} {EventName} failed (attempt {Attempt})", ev.Id, ev.Name, ev.Attempts + 1);
        await this.Mark(ev.Id,
          "UPDATE kernel.outbox_events SET attempts = attempts + 1, last_error = @err, next_attempt_at = now() + make_interval(secs => @secs) WHERE id = @id",
          (e.GetType().Name + ": " + e.Message, seconds), ct);
      }
    }
    return done;
  }

  private async Task<List<Claimed>> Claim(CancellationToken ct)
  {
    // Claim a batch for a lease (so a second replica skips it), outside any business transaction.
    await using var cmd = dataSource.CreateCommand("""
      UPDATE kernel.outbox_events SET next_attempt_at = now() + interval '5 minutes'
      WHERE id IN (SELECT id FROM kernel.outbox_events
                   WHERE dispatched_at IS NULL AND attempts < @max AND next_attempt_at <= now()
                   ORDER BY created_at LIMIT @batch FOR UPDATE SKIP LOCKED)
      RETURNING id, name, payload::text, created_at, attempts
      """);
    cmd.Parameters.AddWithValue("max", options.MaxAttempts);
    cmd.Parameters.AddWithValue("batch", options.BatchSize);
    var list = new List<Claimed>();
    await using var r = await cmd.ExecuteReaderAsync(ct);
    while (await r.ReadAsync(ct))
      list.Add(new Claimed(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetDateTime(3), r.GetInt32(4)));
    return [.. list.OrderBy(e => e.CreatedAt)];
  }

  private async Task Deliver(Claimed ev, SubscriptionDefinition sub, CancellationToken ct)
  {
    await using var scope = scopes.CreateAsyncScope();
    var sp = scope.ServiceProvider;
    var uow = sp.GetRequiredService<IUnitOfWork>();
    var shared = sp.GetRequiredService<SharedConnection>();
    await uow.Start(async () =>
    {
      await using (var has = new NpgsqlCommand(
                     "SELECT 1 FROM kernel.outbox_deliveries WHERE event_id = @e AND subscriber_id = @s", shared.Connection))
      {
        has.Parameters.AddWithValue("e", ev.Id);
        has.Parameters.AddWithValue("s", sub.SubscriberId);
        if (await has.ExecuteScalarAsync(ct) is not null) return;
      }

      var payload = JsonSerializer.Deserialize(ev.Payload, sub.Payload, OutboxStore.Json)
                    ?? throw new InvalidOperationException($"empty payload for {ev.Name}");
      var handler = sp.GetRequiredService(sub.Handler);
      var meta = new OutboxEvent(ev.Id, ev.Name, ev.CreatedAt, ev.Attempts + 1);
      if (sub.Invoke is { } invoke)
      {
        await invoke(handler, meta, payload, ct);
      }
      else
      {
        try
        {
          await (Task)sub.Handler.GetMethod("Handle")!.Invoke(handler, [meta, payload, ct])!;
        }
        catch (System.Reflection.TargetInvocationException e) when (e.InnerException is not null)
        {
          System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(e.InnerException);
        }
      }

      await using var rec = new NpgsqlCommand(
        "INSERT INTO kernel.outbox_deliveries (event_id, subscriber_id) VALUES (@e, @s)", shared.Connection);
      rec.Parameters.AddWithValue("e", ev.Id);
      rec.Parameters.AddWithValue("s", sub.SubscriberId);
      await rec.ExecuteNonQueryAsync(ct);
    }, ct);
  }

  private async Task Mark(Guid id, string sql, (string Err, double Secs)? failure, CancellationToken ct)
  {
    await using var cmd = dataSource.CreateCommand(sql);
    cmd.Parameters.AddWithValue("id", id);
    if (failure is { } f)
    {
      cmd.Parameters.AddWithValue("err", f.Err.Length > 2000 ? f.Err[..2000] : f.Err);
      cmd.Parameters.AddWithValue("secs", f.Secs);
    }
    await cmd.ExecuteNonQueryAsync(ct);
  }
}

/// <summary>Polls the outbox. Never crashes the API.</summary>
public sealed class OutboxHostedService(IOutboxDispatcher dispatcher, OutboxOptions options, ILogger<OutboxHostedService> logger)
  : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (!options.Enabled) return;
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await dispatcher.DispatchOnce(stoppingToken);
      }
      catch (Exception e) when (!stoppingToken.IsCancellationRequested)
      {
        logger.LogError(e, "Outbox dispatch pass failed");
      }
      try
      {
        await Task.Delay(options.PollMilliseconds, stoppingToken);
      }
      catch (OperationCanceledException)
      {
        return;
      }
    }
  }
}
