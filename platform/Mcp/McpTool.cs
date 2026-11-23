using System.Text.Json;
using LazyTax.Platform.Http;

namespace LazyTax.Platform.Mcp;

/// <summary>What a tool handler receives: the same caller and DI scope an HTTP request gets.</summary>
public sealed record ToolCall<TArgs>(Caller Caller, TArgs Args, IServiceProvider Services, CancellationToken Ct);

/// <summary>
/// One MCP tool, served at <c>/mcp</c> (<c>tools/list</c>, <c>tools/call</c>). Same JWT, policy, subject rule
/// and guards as an HTTP route, and it should call the same service method.
/// </summary>
public sealed record McpToolDefinition
{
  /// <summary><c>&lt;mcp prefix&gt;.&lt;verb&gt;</c>, e.g. <c>vacation.create</c>.</summary>
  public required string Name { get; init; }

  public required string Description { get; init; }

  /// <summary>A policy name, as for <c>[Policy]</c>. Required.</summary>
  public required string Policy { get; init; }

  /// <summary>Argument holding the target user id (layer 3), e.g. <c>userId</c>; null when none.</summary>
  public string? Subject { get; init; }

  /// <summary>Layer-5 guards, in order (same rule as <c>[Guards]</c>).</summary>
  public IReadOnlyList<Type> Guards { get; init; } = [];

  /// <summary>JSON schema of the arguments (an object schema).</summary>
  public required JsonElement InputSchema { get; init; }

  public required Type ArgsType { get; init; }

  /// <summary>Untyped handler; build it with <see cref="McpTool.Define{TArgs}"/>.</summary>
  public required Func<Caller, object, IServiceProvider, CancellationToken, Task<object?>> Invoke { get; init; }
}

public static class McpTool
{
  /// <summary>
  /// Typed helper: <c>c.Tool(McpTool.Define&lt;CreateArgs&gt;("vacation.create", "Create a vacation", Policies.Registered,
  /// call =&gt; call.Services.GetRequiredService&lt;VacationService&gt;().Create(...), subject: "userId"))</c>.
  /// The input schema is derived from <typeparamref name="TArgs"/>'s public properties (camelCase).
  /// </summary>
  public static McpToolDefinition Define<TArgs>(
    string name,
    string description,
    string policy,
    Func<ToolCall<TArgs>, Task<object?>> handler,
    string? subject = null,
    params Type[] guards) where TArgs : class
  {
    return new McpToolDefinition
    {
      Name = name,
      Description = description,
      Policy = policy,
      Subject = subject,
      Guards = guards,
      ArgsType = typeof(TArgs),
      InputSchema = McpSchema.For(typeof(TArgs)),
      Invoke = (caller, args, sp, ct) => handler(new ToolCall<TArgs>(caller, (TArgs)args, sp, ct)),
    };
  }
}

/// <summary>A minimal JSON schema from a record's properties (string, number, integer, boolean, array, object).</summary>
public static class McpSchema
{
  public static JsonElement For(Type t)
  {
    var props = new Dictionary<string, object>();
    var required = new List<string>();
    foreach (var p in t.GetProperties())
    {
      var name = JsonNamingPolicy.CamelCase.ConvertName(p.Name);
      var type = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
      props[name] = new { type = JsonType(type) };
      var nullable = Nullable.GetUnderlyingType(p.PropertyType) is not null
                     || new System.Reflection.NullabilityInfoContext().Create(p).WriteState is System.Reflection.NullabilityState.Nullable;
      if (!nullable) required.Add(name);
    }
    return JsonSerializer.SerializeToElement(new { type = "object", properties = props, required });
  }

  private static string JsonType(Type t) => t switch
  {
    _ when t == typeof(string) || t == typeof(Guid) || t == typeof(DateOnly) || t == typeof(DateTime) => "string",
    _ when t == typeof(bool) => "boolean",
    _ when t == typeof(int) || t == typeof(long) => "integer",
    _ when t == typeof(decimal) || t == typeof(double) => "number",
    _ when t.IsArray || (t.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(t)) => "array",
    _ => "object",
  };
}
