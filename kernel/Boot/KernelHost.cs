using LazyTax.Kernel.Logging;
using System.Reflection;
using LazyTax.Kernel.Data;
using LazyTax.Kernel.Http;
using LazyTax.Kernel.Mcp;
using LazyTax.Kernel.Outbox;
using LazyTax.Platform.Context;
using LazyTax.Platform.Data;
using LazyTax.Platform.Guards;
using LazyTax.Platform.Http;
using LazyTax.Platform.Outbox;
using LazyTax.Platform.Problems;
using LazyTax.Platform.Workers;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using YamlDotNet.RepresentationModel;

namespace LazyTax.Kernel.Boot;

/// <summary>What the kernel learnt from every context's registration.</summary>
public sealed record KernelModel(
  IReadOnlyList<(ContextManifest Manifest, ContextBuilder Builder)> Contexts,
  IReadOnlyList<McpToolEntry> Tools,
  IReadOnlyDictionary<Assembly, string> ContextByAssembly);

public static class KernelHost
{
  /// <summary>Builds the app: validates every context.yaml, registers contexts in needs order, wires auth, MVC, MCP, outbox.</summary>
  public static WebApplication Build(string[] args, IReadOnlyList<IContext> contexts, Action<WebApplicationBuilder>? configure = null)
  {
    var builder = WebApplication.CreateBuilder(args);
    configure?.Invoke(builder);
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(o => o.IncludeScopes = false);

    var ordered = CheckManifests(contexts);
    var problems = new List<ContextProblem>();

    // Data: one data source; one shared connection, unit of work and outbox per scope.
    builder.Services.AddSingleton(sp => NpgsqlDataSource.Create(sp.GetRequiredService<IConfiguration>().GetConnectionString("Main")
      ?? throw new InvalidOperationException("ConnectionStrings:Main is not set")));
    builder.Services.AddScoped<SharedConnection>();
    builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
    builder.Services.AddScoped<IOutbox, OutboxStore>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<HttpCallerAccessor>();
    builder.Services.AddScoped<ICallerAccessor>(sp => sp.GetRequiredService<HttpCallerAccessor>());

    // Contexts, in needs order.
    var registered = new List<(ContextManifest, ContextBuilder)>();
    foreach (var (m, ctx) in ordered)
    {
      var cb = new ContextBuilder(m, builder.Services, builder.Configuration);
      ctx.Register(cb);
      CheckRegistration(m, cb, problems);
      registered.Add((m, cb));
    }

    var tools = registered.SelectMany(r => r.Item2.Tools.Select(t => new McpToolEntry(r.Item1.Name, t))).ToList();
    foreach (var dup in tools.GroupBy(t => t.Tool.Name).Where(g => g.Count() > 1))
      problems.Add(new(dup.First().Context, "", $"MCP tool {dup.Key} is registered twice", "give each tool its own name"));
    ThrowIfAny(problems);

    var byAssembly = registered.ToDictionary(r => FindAssembly(contexts, r.Item1), r => r.Item1.Name);
    var prefixes = registered.Where(r => r.Item1.HttpPrefix is not null)
      .ToDictionary(r => FindAssembly(contexts, r.Item1), r => r.Item1.HttpPrefix!);
    var model = new KernelModel(registered, tools, byAssembly);
    builder.Services.AddSingleton(model);
    builder.Services.AddSingleton(new DbRegistry([.. registered.SelectMany(r => r.Item2.Dbs)]));

    // Outbox dispatcher.
    builder.Services.AddSingleton(sp => sp.GetRequiredService<IConfiguration>().GetSection("Outbox").Get<OutboxOptions>() ?? new OutboxOptions());
    builder.Services.AddSingleton<IReadOnlyList<SubscriptionDefinition>>([.. registered.SelectMany(r => r.Item2.Subscriptions)]);
    builder.Services.AddSingleton<IOutboxDispatcher, OutboxDispatcher>();
    builder.Services.AddHostedService<OutboxHostedService>();
    builder.Services.AddHostedService<WorkerHost>();

    // HTTP: auth layers 1-2, then MVC with the subject and guard filter (3, 5) and problem mapping.
    builder.Services.AddKernelAuth();
    var mvc = builder.Services.AddControllers(o =>
    {
      o.Conventions.Add(new ContextRoutePrefixConvention(prefixes));
      o.Filters.Add<AuthLayersFilter>();
      o.Filters.Add<ProblemExceptionFilter>();
    });
    mvc.PartManager.ApplicationParts.Clear();
    foreach (var (m, cb) in registered.Where(r => r.Item2.HasControllers))
      mvc.AddApplicationPart(FindAssembly(contexts, m));

    builder.Services.AddSingleton(sp => new RequestLog(Environment.GetEnvironmentVariable("LOG_FILE") ?? sp.GetRequiredService<IConfiguration>()["LOG_FILE"]));

    var app = builder.Build();
    app.UseMiddleware<RequestLogMiddleware>(model.ContextByAssembly);
    app.UseMiddleware<ProblemMiddleware>();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.MapPost("/mcp", http => McpEndpoint.Handle(http, tools)).RequireAuthorization(Policies.Public)
      .WithMetadata(new PolicyAttribute(Policies.Public));
    app.MapGet("/health", () => Results.Ok(new { status = "ok" })).RequireAuthorization(Policies.Public)
      .WithMetadata(new PolicyAttribute(Policies.Public));
    return app;
  }

  /// <summary>Reads and checks every context.yaml (fields, needs, uses, cycles) and returns contexts in needs order.</summary>
  public static List<(ContextManifest Manifest, IContext Context)> CheckManifests(IReadOnlyList<IContext> contexts)
  {
    var problems = new List<ContextProblem>();
    var read = contexts.Select(c => ManifestLoader.Read(c, problems)).ToList();
    var ordered = ManifestLoader.Order(read, LoadDependencies(), problems);
    ThrowIfAny(problems);
    return ordered;
  }

  /// <summary>
  /// Start-up checks that need the built app: every endpoint has an explicit [Policy], guard order is valid,
  /// every DbContext uses its context's schema. Throws <see cref="ContextBootException"/>.
  /// </summary>
  public static void Verify(IServiceProvider services, IEnumerable<EndpointDataSource>? sources = null)
  {
    var problems = new List<ContextProblem>();
    var model = services.GetRequiredService<KernelModel>();
    var endpoints = PolicyTable.Endpoints(services, sources);
    if (!endpoints.Any(e => e.Kind == "http" && e.Context != "kernel") && model.Contexts.Any(c => c.Builder.HasControllers))
      throw new InvalidOperationException("Verify saw no context routes: pass the app's DataSources before it starts");
    foreach (var e in endpoints)
    {
      if (e.Policy is null)
        problems.Add(new(e.Context, $"contexts/{e.Context}", $"{e.Method} {e.Path} has no [Policy]",
          "add [Policy(Policies.Registered)] (or Public/Admin) to the action or its controller"));
    }

    using var scope = services.CreateScope();
    foreach (var (m, cb) in model.Contexts)
    {
      foreach (var t in cb.Dbs)
      {
        var db = (DbContext)scope.ServiceProvider.GetRequiredService(t);
        var schema = db.Model.GetDefaultSchema();
        if (schema != m.Name)
          problems.Add(new(m.Name, m.Folder, $"{t.Name} uses schema '{schema ?? "public"}'",
            $"call modelBuilder.HasDefaultSchema(\"{m.Name}\") first in OnModelCreating"));
      }
      foreach (var tool in cb.Tools)
        CheckGuards(m, $"tool {tool.Name}", tool.Guards, scope.ServiceProvider, problems);
    }
    foreach (var e in endpoints)
      if (e.GuardTypes.Count > 0)
      {
        var m = model.Contexts.FirstOrDefault(c => c.Manifest.Name == e.Context).Manifest;
        if (m is not null) CheckGuards(m, $"{e.Method} {e.Path}", e.GuardTypes, scope.ServiceProvider, problems);
      }
    ThrowIfAny(problems);
  }

  /// <summary>Creates the kernel schema and runs every context's EF migrations (each in its own schema).</summary>
  public static async Task Migrate(IServiceProvider services, CancellationToken ct = default)
  {
    var ds = services.GetRequiredService<NpgsqlDataSource>();
    await using (var cmd = ds.CreateCommand(KernelSchema.Sql)) await cmd.ExecuteNonQueryAsync(ct);
    var model = services.GetRequiredService<KernelModel>();
    foreach (var (_, cb) in model.Contexts)
      foreach (var t in cb.Dbs)
      {
        await using var scope = services.CreateAsyncScope();
        var db = (DbContext)scope.ServiceProvider.GetRequiredService(t);
        await db.Database.MigrateAsync(ct);
      }
  }

  private static void CheckGuards(ContextManifest m, string where, IReadOnlyList<Type> guards, IServiceProvider sp, List<ContextProblem> problems)
  {
    var last = 0;
    foreach (var t in guards)
    {
      if (sp.GetService(t) is not IGuard g)
      {
        problems.Add(new(m.Name, m.Folder, $"{where}: guard {t.Name} is not registered or is not an IGuard",
          $"c.Services.AddScoped<{t.Name}>() in Context.cs"));
        continue;
      }
      if ((int)g.Stage < last)
        problems.Add(new(m.Name, m.Folder, $"{where}: guard {t.Name} ({g.Stage}) runs after a later stage",
          "order guards plan first, then debt (DESIGN.md section 4)"));
      last = (int)g.Stage;
    }
  }

  private static void CheckRegistration(ContextManifest m, ContextBuilder cb, List<ContextProblem> problems)
  {
    var provided = cb.Provided.Select(p => p.Name).ToHashSet();
    foreach (var p in m.Provides.Where(p => !provided.Contains(p)))
      problems.Add(new(m.Name, m.Folder, $"context.yaml provides {p}, but Context.cs does not implement it",
        $"add c.Provide<I..., ...>(\"{p}\") to Context.cs, or remove it from provides"));
    foreach (var p in provided.Where(p => !m.Provides.Contains(p)))
      problems.Add(new(m.Name, m.Folder, $"Context.cs provides {p}, which context.yaml does not list", $"add {p} to provides"));
    if (cb.HasControllers && m.HttpPrefix is null)
      problems.Add(new(m.Name, m.Folder, "it has controllers but no http.prefix", $"add http: {{ prefix: /api/v1/{m.Name} }}"));
    foreach (var t in cb.Tools)
    {
      if (m.McpPrefix is null || !t.Name.StartsWith(m.McpPrefix + ".", StringComparison.Ordinal))
        problems.Add(new(m.Name, m.Folder, $"MCP tool '{t.Name}' is not named {m.McpPrefix ?? m.Name}.<verb>",
          $"set mcp: {{ prefix: {m.Name} }} and name tools {m.Name}.<verb>"));
      if (string.IsNullOrWhiteSpace(t.Policy))
        problems.Add(new(m.Name, m.Folder, $"MCP tool '{t.Name}' has no policy", "set Policy (Policies.Registered, Public or Admin)"));
    }
    foreach (var s in cb.Subscriptions)
      if (!ManifestLoader.EventRx().IsMatch(s.EventName))
        problems.Add(new(m.Name, m.Folder, $"subscribes to '{s.EventName}'", "name events <context>.<event>, e.g. check.settled"));
  }

  private static Assembly FindAssembly(IReadOnlyList<IContext> contexts, ContextManifest m) =>
    ManifestLoader.ContextAssembly(contexts.First(c => c is PendingContext p ? p.Name == m.Name
      : c.GetType().FullName == $"Ctx.{ManifestLoader.Pascal(m.Name)}.{ManifestLoader.Pascal(m.Name)}Context"));

  private static HashSet<string> LoadDependencies()
  {
    var path = Path.Combine(AppContext.BaseDirectory, "dependency.yaml");
    if (!File.Exists(path)) return [];
    var yaml = new YamlStream();
    using var reader = new StreamReader(path);
    yaml.Load(reader);
    if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root) return [];
    return [.. root.Children.Keys.OfType<YamlScalarNode>().Select(k => k.Value!)];
  }

  private static void ThrowIfAny(List<ContextProblem> problems)
  {
    if (problems.Count > 0) throw new ContextBootException([.. problems]);
  }
}

/// <summary>Problems thrown outside MVC (middleware, MCP plumbing) and unexpected errors become problem details.</summary>
public sealed class ProblemMiddleware(RequestDelegate next, ILogger<ProblemMiddleware> logger, IHostEnvironment env)
{
  public async Task Invoke(HttpContext context)
  {
    try
    {
      await next(context);
    }
    catch (Problem p) when (!context.Response.HasStarted)
    {
      await ProblemWriter.Write(context, p);
    }
    catch (Exception e) when (!context.Response.HasStarted && e is not OperationCanceledException)
    {
      logger.LogError(e, "Unhandled error on {Path}", context.Request.Path.Value);
      // In Development (and tests) the detail names the exception, to find bugs fast; never in production.
      await ProblemWriter.Write(context, new InternalError(env.IsDevelopment() ? $"{e.GetType().Name}: {e.Message}" : "Something went wrong."));
    }
  }

  private sealed class InternalError(string detail) : Problem(500, "internal", "Internal Error", detail);
}

/// <summary>Runs every context's <see cref="IContextWorker"/> on its interval, each tick in a fresh scope.</summary>
public sealed class WorkerHost(IServiceScopeFactory scopes, IEnumerable<WorkerDefinition> workers, IConfiguration config, ILogger<WorkerHost> logger)
  : BackgroundService
{
  protected override Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (!config.GetValue("Workers:Enabled", true)) return Task.CompletedTask;
    return Task.WhenAll(workers.Select(w => this.Loop(w, stoppingToken)));
  }

  private async Task Loop(WorkerDefinition w, CancellationToken ct)
  {
    while (!ct.IsCancellationRequested)
    {
      TimeSpan interval = TimeSpan.FromMinutes(1);
      try
      {
        await using var scope = scopes.CreateAsyncScope();
        var worker = (IContextWorker)scope.ServiceProvider.GetRequiredService(w.Worker);
        interval = worker.Interval;
        await worker.Tick(ct);
      }
      catch (Exception e) when (!ct.IsCancellationRequested)
      {
        logger.LogError(e, "Worker {Context}/{Worker} tick failed", w.Context, w.Worker.Name);
      }
      try
      {
        await Task.Delay(interval, ct);
      }
      catch (OperationCanceledException)
      {
        return;
      }
    }
  }
}
