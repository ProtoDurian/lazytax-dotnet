using LazyTax.Platform.Data;
using LazyTax.Platform.Lifecycle;
using LazyTax.Platform.Mcp;
using LazyTax.Platform.Outbox;
using LazyTax.Platform.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LazyTax.Platform.Context;

/// <summary>
/// What a context's <see cref="IContext.Register"/> may declare. Everything a context owns is declared
/// here and nowhere else: services, contract implementations, its DbContext, controllers, MCP tools,
/// workers, outbox subscribers and the account-deletion hooks.
/// </summary>
public sealed class ContextBuilder(ContextManifest manifest, IServiceCollection services, IConfiguration configuration)
{
  private readonly List<ProvidedContract> provided = [];
  private readonly List<Type> dbs = [];
  private readonly List<McpToolDefinition> tools = [];
  private readonly List<SubscriptionDefinition> subscriptions = [];

  public ContextManifest Manifest { get; } = manifest;

  /// <summary>Plain DI for the context's own services, repositories and guards.</summary>
  public IServiceCollection Services { get; } = services;

  /// <summary>The whole app configuration. A context's own settings live under <c>Contexts:&lt;name&gt;</c>.</summary>
  public IConfiguration Configuration { get; } = configuration;

  /// <summary>This context's own section: <c>Contexts:&lt;name&gt;</c>.</summary>
  public IConfigurationSection Settings => this.Configuration.GetSection($"Contexts:{this.Manifest.Name}");

  public bool HasControllers { get; private set; }

  public IReadOnlyList<ProvidedContract> Provided => this.provided;
  public IReadOnlyList<Type> Dbs => this.dbs;
  public IReadOnlyList<McpToolDefinition> Tools => this.tools;
  public IReadOnlyList<SubscriptionDefinition> Subscriptions => this.subscriptions;

  /// <summary>
  /// Implement a contract from <c>provides:</c>. <paramref name="contract"/> must match context.yaml
  /// (e.g. <c>"plan.v1"</c>). Scoped: one instance per request or unit of work.
  /// </summary>
  public ContextBuilder Provide<TContract, TImpl>(string contract)
    where TContract : class
    where TImpl : class, TContract
  {
    this.Services.AddScoped<TContract, TImpl>();
    this.provided.Add(new ProvidedContract(contract, typeof(TContract), typeof(TImpl)));
    return this;
  }

  /// <summary>Kernel use only: a placeholder for a contract whose context is not built yet.</summary>
  public ContextBuilder ProvidePending(string contract, Type contractType, Func<object> factory)
  {
    this.Services.AddScoped(contractType, _ => factory());
    this.provided.Add(new ProvidedContract(contract, contractType, contractType));
    return this;
  }

  /// <summary>
  /// The context's one DbContext. It is built on the request's shared connection, so every context's
  /// writes join one transaction. Its model must call <c>HasDefaultSchema("&lt;name&gt;")</c>; migrations keep
  /// their history in <c>&lt;name&gt;.__EFMigrationsHistory</c>.
  /// </summary>
  public ContextBuilder Db<TDb>() where TDb : DbContext
  {
    var schema = this.Manifest.Name;
    this.Services.AddDbContext<TDb>((sp, o) => o.UseNpgsql(
      sp.GetRequiredService<SharedConnection>().Connection,
      n => n.MigrationsHistoryTable("__EFMigrationsHistory", schema)));
    this.dbs.Add(typeof(TDb));
    return this;
  }

  /// <summary>Serve this assembly's controllers (every action needs a [Policy]).</summary>
  public ContextBuilder AddControllers()
  {
    this.HasControllers = true;
    return this;
  }

  /// <summary>An MCP tool; see <see cref="McpTool"/> for the typed helper.</summary>
  public ContextBuilder Tool(McpToolDefinition tool)
  {
    this.tools.Add(tool);
    return this;
  }

  /// <summary>A background job; the kernel runs each tick in a fresh scope and never lets it crash the API.</summary>
  public ContextBuilder Worker<TWorker>() where TWorker : class, IContextWorker
  {
    this.Services.AddScoped<TWorker>();
    this.Services.AddSingleton(new WorkerDefinition(this.Manifest.Name, typeof(TWorker)));
    return this;
  }

  /// <summary>
  /// Deliver outbox events named <paramref name="eventName"/> (<c>"&lt;context&gt;.&lt;event&gt;"</c>, e.g.
  /// <c>"check.settled"</c>) to <typeparamref name="THandler"/>. The payload is JSON; <typeparamref name="TPayload"/>
  /// is the subscriber's own record of the fields it reads. Each delivery runs in its own unit of work.
  /// </summary>
  public ContextBuilder Subscribe<TPayload, THandler>(string eventName)
    where TPayload : class
    where THandler : class, IOutboxSubscriber<TPayload>
  {
    this.Services.AddScoped<THandler>();
    this.subscriptions.Add(new SubscriptionDefinition(
      eventName, $"{this.Manifest.Name}:{typeof(THandler).Name}", typeof(TPayload), typeof(THandler))
    {
      Invoke = (h, meta, payload, ct) => ((THandler)h).Handle(meta, (TPayload)payload, ct),
    });
    return this;
  }

  /// <summary>Runs before deletion; throw a <c>Problem</c> to block it (penalty: outstanding debt).</summary>
  public ContextBuilder DeleteGuard<T>() where T : class, IDeleteGuard
  {
    this.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IDeleteGuard, T>());
    return this;
  }

  /// <summary>Runs after the guards pass, OUTSIDE the transaction, best effort (payment: revoke consents).</summary>
  public ContextBuilder BeforePurge<T>() where T : class, IBeforePurge
  {
    this.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IBeforePurge, T>());
    return this;
  }

  /// <summary>Deletes every row of the user in this context, INSIDE account's one unit of work. Every context with user data registers one.</summary>
  public ContextBuilder PurgeUser<T>() where T : class, IUserPurge
  {
    this.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IUserPurge, T>());
    return this;
  }
}

public sealed record ProvidedContract(string Name, Type Contract, Type Implementation);

public sealed record SubscriptionDefinition(string EventName, string SubscriberId, Type Payload, Type Handler)
{
  /// <summary>Calls the handler without reflection: (handler, meta, payload, ct). Set by <see cref="ContextBuilder.Subscribe{TPayload, THandler}"/>.</summary>
  public Func<object, OutboxEvent, object, CancellationToken, Task>? Invoke { get; init; }
}

public sealed record WorkerDefinition(string Context, Type Worker);
