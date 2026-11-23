using LazyTax.Kernel;
using LazyTax.Kernel.Boot;
using LazyTax.Kernel.Outbox;
using LazyTax.Platform.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Npgsql;
using Xunit;

namespace LazyTax.Testing;

/// <summary>
/// The whole app (every context) on real Postgres, in-process. One per test assembly:
/// <c>[assembly: AssemblyFixture(typeof(KernelApp))]</c>, then take <c>KernelApp app</c> in a test class constructor.
/// Postgres comes from <c>LT_PG</c> (a connection string without Database), default
/// <c>Host=localhost;Port=$LT_PG_PORT (55450);Username=postgres;Password=postgres</c>; start it with
/// <c>task pg:up</c>. Each fixture creates its own fresh database, so parallel test projects never clash.
/// The outbox dispatcher and workers are off; call <see cref="DispatchOutbox"/>.
/// </summary>
public class KernelApp : WebApplicationFactory<Program>, IAsyncLifetime
{
  public string Database { get; } = "lt_test_" + Guid.NewGuid().ToString("N")[..12];

  public string ConnectionString { get; private set; } = string.Empty;

  public static string ServerConnectionString =>
    Environment.GetEnvironmentVariable("LT_PG")
    ?? $"Host=localhost;Port={Environment.GetEnvironmentVariable("LT_PG_PORT") ?? "55450"};Username=postgres;Password=postgres";

  public async ValueTask InitializeAsync()
  {
    await using (var admin = new NpgsqlConnection(ServerConnectionString + ";Database=postgres"))
    {
      await admin.OpenAsync();
      await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{this.Database}\"", admin);
      await cmd.ExecuteNonQueryAsync();
    }
    this.ConnectionString = ServerConnectionString + $";Database={this.Database}";
    try
    {
      _ = this.Server; // build the host (WebApplicationFactory stops Program at Build, so check and migrate here)
    }
    catch (InvalidOperationException e) when (e.Message.Contains("without ever building", StringComparison.Ordinal))
    {
      // Program refused to boot; rebuild here to surface the named context problems instead.
      KernelHost.Build([], ContextRegistry.All());
      throw;
    }
    KernelHost.Verify(this.Services);
    await KernelHost.Migrate(this.Services);
  }

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseSetting("ConnectionStrings:Main", this.ConnectionString);
    builder.UseSetting("Auth:Issuer", TestTokens.Issuer);
    builder.UseSetting("Auth:Audience", TestTokens.Audience);
    builder.UseSetting("Outbox:Enabled", "false");
    builder.UseSetting("Workers:Enabled", "false");
    builder.ConfigureServices(this.ConfigureTestServices);
    builder.ConfigureServices(s => s.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, j =>
    {
      // Same validation as production, but the key comes from TestTokens instead of a JWKS fetch.
      j.Configuration = new OpenIdConnectConfiguration { Issuer = TestTokens.Issuer };
      j.Configuration.SigningKeys.Add(TestTokens.Key);
      j.TokenValidationParameters.IssuerSigningKey = TestTokens.Key;
    }));
  }

  /// <summary>Override to add or replace services for one fixture (e.g. a test delete guard).</summary>
  protected virtual void ConfigureTestServices(IServiceCollection services)
  {
  }

  /// <summary>An HTTP client signed in as <paramref name="sub"/>.</summary>
  public HttpClient ClientAs(string sub, params string[] scopes) => this.CreateClient().As(sub, scopes);

  /// <summary>An HTTP client with no token.</summary>
  public HttpClient Anonymous() => this.CreateClient();

  /// <summary>Delivers every due outbox event now; returns how many were dispatched.</summary>
  public Task<int> DispatchOutbox() => this.Services.GetRequiredService<IOutboxDispatcher>().DispatchOnce();

  /// <summary>Runs <paramref name="work"/> in a fresh DI scope (one shared connection), e.g. to call a contract.</summary>
  public async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> work)
  {
    await using var scope = this.Services.CreateAsyncScope();
    return await work(scope.ServiceProvider);
  }

  public async Task InScope(Func<IServiceProvider, Task> work)
  {
    await using var scope = this.Services.CreateAsyncScope();
    await work(scope.ServiceProvider);
  }

  /// <summary>Runs a scalar SQL query on the test database (for "0 rows left" assertions).</summary>
  public async Task<long> Count(string sql, params (string Name, object Value)[] args)
  {
    await using var conn = new NpgsqlConnection(this.ConnectionString);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand(sql, conn);
    foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
    return Convert.ToInt64(await cmd.ExecuteScalarAsync());
  }

  public new async ValueTask DisposeAsync()
  {
    GC.SuppressFinalize(this);
    await base.DisposeAsync();
    NpgsqlConnection.ClearAllPools();
    await using var admin = new NpgsqlConnection(ServerConnectionString + ";Database=postgres");
    await admin.OpenAsync();
    await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{this.Database}\" WITH (FORCE)", admin);
    await cmd.ExecuteNonQueryAsync();
  }
}
