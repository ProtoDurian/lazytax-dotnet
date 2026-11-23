using LazyTax.Kernel.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;
using LazyTax.Kernel.Http;
using LazyTax.Platform.Guards;
using LazyTax.Platform.Http;
using LazyTax.Platform.Mcp;
using LazyTax.Platform.Problems;
using Microsoft.AspNetCore.Authorization;

namespace LazyTax.Kernel.Mcp;

public sealed record McpToolEntry(string Context, McpToolDefinition Tool);

/// <summary>
/// The one MCP endpoint at <c>/mcp</c>: a minimal JSON-RPC 2.0 subset of the streamable-HTTP transport
/// (<c>initialize</c>, <c>notifications/initialized</c>, <c>ping</c>, <c>tools/list</c>, <c>tools/call</c>), plain JSON
/// responses. Each tool runs the same layers as a route: JWT (layer 1, middleware), its policy (2), its
/// subject argument (3) and its guards (5); the handler then calls the same service method as the route.
/// </summary>
public static class McpEndpoint
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public static async Task Handle(HttpContext http, IReadOnlyList<McpToolEntry> tools)
  {
    JsonNode? req;
    try
    {
      req = await JsonNode.ParseAsync(http.Request.Body);
    }
    catch (JsonException)
    {
      await Reply(http, null, error: new { code = -32700, message = "Parse error" });
      return;
    }

    var id = req?["id"]?.DeepClone();
    var method = req?["method"]?.GetValue<string>();
    switch (method)
    {
      case "initialize":
        await Reply(http, id, new
        {
          protocolVersion = req?["params"]?["protocolVersion"]?.GetValue<string>() ?? "2025-06-18",
          capabilities = new { tools = new { listChanged = false } },
          serverInfo = new { name = "lazytax", version = "0.1.0" },
        });
        return;
      case "notifications/initialized":
        http.Response.StatusCode = StatusCodes.Status202Accepted;
        return;
      case "ping":
        await Reply(http, id, new { });
        return;
      case "tools/list":
        await Reply(http, id, new
        {
          tools = tools.Select(t => new { name = t.Tool.Name, description = t.Tool.Description, inputSchema = t.Tool.InputSchema }),
        });
        return;
      case "tools/call":
        await Call(http, id, req?["params"], tools);
        return;
      default:
        await Reply(http, id, error: new { code = -32601, message = $"Method not found: {method}" });
        return;
    }
  }

  private static async Task Call(HttpContext http, JsonNode? id, JsonNode? p, IReadOnlyList<McpToolEntry> tools)
  {
    var name = p?["name"]?.GetValue<string>();
    var entry = tools.FirstOrDefault(t => t.Tool.Name == name);
    if (entry is null)
    {
      await Reply(http, id, error: new { code = -32602, message = $"Unknown tool: {name}" });
      return;
    }

    var tool = entry.Tool;
    http.Items[RequestLog.ToolKey] = tool.Name;
    http.Items[RequestLog.ContextKey] = entry.Context;
    var sp = http.RequestServices;
    try
    {
      var caller = ClaimsFields.ToCaller(http.User);
      // Layer 2: the tool's own policy.
      var auth = await sp.GetRequiredService<IAuthorizationService>().AuthorizeAsync(http.User, tool.Policy);
      if (!auth.Succeeded) throw caller.IsAuthenticated ? new Forbidden() : new Unauthenticated();

      var argsNode = p?["arguments"] ?? new JsonObject();
      object args;
      try
      {
        args = argsNode.Deserialize(tool.ArgsType, Json) ?? throw new JsonException("null arguments");
      }
      catch (JsonException e)
      {
        throw new ValidationFailed("Invalid tool arguments", "arguments", e.Message);
      }

      var values = (argsNode as JsonObject)?.ToDictionary(k => k.Key, v => v.Value is JsonValue jv ? jv.ToString() : v.Value?.ToJsonString())
                   ?? [];
      // Layer 3: the subject argument must be the caller, unless admin.
      if (tool.Subject is { } subject) SubjectRule.Check(caller, values.GetValueOrDefault(subject));
      // Layer 5: declared guards, in order.
      await GuardRunner.Run(tool.Guards, new GuardContext(caller, values, sp), http.RequestAborted);

      (sp.GetRequiredService<ICallerAccessor>() as HttpCallerAccessor)?.Set(caller);
      var result = await tool.Invoke(caller, args, sp, http.RequestAborted);
      var text = JsonSerializer.Serialize(result, Json);
      await Reply(http, id, new
      {
        content = new[] { new { type = "text", text } },
        structuredContent = result is null ? null : JsonSerializer.SerializeToNode(result, Json),
        isError = false,
      });
    }
    catch (Problem problem)
    {
      // The JSON-RPC call still answers 200; the problem goes in the result and in the request log.
      http.Items[RequestLog.ProblemKey] = problem.Id;
      http.Items["lazytax.toolStatus"] = problem.Status;
      var body = ProblemWriter.ToBody(problem, http.TraceIdentifier);
      await Reply(http, id, new
      {
        content = new[] { new { type = "text", text = JsonSerializer.Serialize(body, Json) } },
        structuredContent = body,
        isError = true,
      });
    }
  }

  private static async Task Reply(HttpContext http, JsonNode? id, object? result = null, object? error = null)
  {
    http.Response.ContentType = "application/json";
    var msg = error is null
      ? (object)new { jsonrpc = "2.0", id, result }
      : new { jsonrpc = "2.0", id, error };
    await http.Response.WriteAsync(JsonSerializer.Serialize(msg, Json));
  }
}
