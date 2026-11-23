using System.Text.Json;
using System.Transactions;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;
using Npgsql;
using NpgsqlTypes;

namespace LazyTax.Kernel.Outbox;

/// <summary>The kernel schema: the outbox and its per-subscriber deliveries. Idempotent DDL, run at start.</summary>
public static class KernelSchema
{
  public const string Sql = """
    CREATE SCHEMA IF NOT EXISTS kernel;
    CREATE TABLE IF NOT EXISTS kernel.outbox_events (
      id uuid PRIMARY KEY,
      name text NOT NULL,
      payload jsonb NOT NULL,
      created_at timestamptz NOT NULL DEFAULT now(),
      attempts int NOT NULL DEFAULT 0,
      next_attempt_at timestamptz NOT NULL DEFAULT now(),
      dispatched_at timestamptz NULL,
      last_error text NULL
    );
    CREATE INDEX IF NOT EXISTS outbox_events_due ON kernel.outbox_events (next_attempt_at) WHERE dispatched_at IS NULL;
    CREATE TABLE IF NOT EXISTS kernel.outbox_deliveries (
      event_id uuid NOT NULL REFERENCES kernel.outbox_events(id) ON DELETE CASCADE,
      subscriber_id text NOT NULL,
      delivered_at timestamptz NOT NULL DEFAULT now(),
      PRIMARY KEY (event_id, subscriber_id)
    );
    """;
}

/// <summary><see cref="IOutbox"/>: an INSERT on the scope's shared connection, inside the current unit of work.</summary>
public sealed class OutboxStore(SharedConnection shared) : IOutbox
{
  internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public async Task Add(string eventName, object payload, CancellationToken ct = default)
  {
    if (Transaction.Current is null)
      throw new InvalidOperationException($"outbox.Add({eventName}) must run inside IUnitOfWork.Start");
    await using var cmd = new NpgsqlCommand(
      "INSERT INTO kernel.outbox_events (id, name, payload) VALUES (@id, @name, @payload)", shared.Connection);
    cmd.Parameters.AddWithValue("id", Guid.CreateVersion7());
    cmd.Parameters.AddWithValue("name", eventName);
    cmd.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(payload, Json) });
    await cmd.ExecuteNonQueryAsync(ct);
  }
}
