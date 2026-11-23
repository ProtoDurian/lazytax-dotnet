using Npgsql;

namespace LazyTax.Platform.Data;

/// <summary>
/// ONE Postgres connection per request or unit of work (a DI scope). Every context's DbContext is built
/// on it, so a transaction across contexts stays on one connection and never promotes to a distributed
/// transaction (Npgsql cannot). Contexts never use this directly; <see cref="ContextBuilder"/> wires it.
/// </summary>
public sealed class SharedConnection(NpgsqlDataSource dataSource) : IAsyncDisposable, IDisposable
{
  private NpgsqlConnection? connection;

  public NpgsqlConnection Connection => this.connection ??= dataSource.CreateConnection();

  public void Dispose() => this.connection?.Dispose();

  public async ValueTask DisposeAsync()
  {
    if (this.connection is not null) await this.connection.DisposeAsync();
  }
}
