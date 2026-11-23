using System.Diagnostics;
using System.Text.Json;
using LazyTax.Kernel.Http;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace LazyTax.Kernel.Logging;

/// <summary>
/// One JSON line per request (HTTP route or MCP tool call) to stdout, and to <c>LOG_FILE</c> when set:
/// <c>time, level, context, route|tool, sub, requestId, status, durationMs, problem</c>.
/// Never tokens, emails or bodies. 4xx/5xx log at level "error" with the problem type.
/// </summary>
public sealed class RequestLog : IDisposable
{
  public const string ProblemKey = "lazytax.problem";
  public const string ToolKey = "lazytax.tool";
  public const string ContextKey = "lazytax.context";

  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private readonly Lock gate = new();
  private readonly StreamWriter? file;

  public RequestLog(string? logFile)
  {
    if (string.IsNullOrWhiteSpace(logFile)) return;
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logFile))!);
    this.file = new StreamWriter(new FileStream(logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
  }

  public void Write(IReadOnlyDictionary<string, object?> line)
  {
    var text = JsonSerializer.Serialize(line, Json);
    lock (this.gate)
    {
      Console.Out.WriteLine(text);
      this.file?.WriteLine(text);
    }
  }

  public void Dispose() => this.file?.Dispose();
}

public sealed class RequestLogMiddleware(RequestDelegate next, RequestLog log, IReadOnlyDictionary<System.Reflection.Assembly, string> contextByAssembly)
{
  public async Task Invoke(HttpContext context)
  {
    var sw = Stopwatch.StartNew();
    var failed = false;
    try
    {
      await next(context);
    }
    catch
    {
      failed = true;
      throw;
    }
    finally
    {
      var endpoint = context.GetEndpoint();
      var action = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
      var ctxName = context.Items[RequestLog.ContextKey] as string
                    ?? (action is not null ? contextByAssembly.GetValueOrDefault(action.ControllerTypeInfo.Assembly) : null)
                    ?? "kernel";
      var status = failed ? 500 : context.Items["lazytax.toolStatus"] as int? ?? context.Response.StatusCode;
      var tool = context.Items[RequestLog.ToolKey] as string;
      log.Write(new Dictionary<string, object?>
      {
        ["time"] = DateTime.UtcNow.ToString("O"),
        ["level"] = status >= 400 ? "error" : "info",
        ["context"] = ctxName,
        ["method"] = context.Request.Method,
        ["route"] = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? context.Request.Path.Value,
        ["tool"] = tool,
        ["sub"] = ClaimsFields.ToCaller(context.User).Sub,
        ["requestId"] = context.TraceIdentifier,
        ["status"] = status,
        ["durationMs"] = Math.Round(sw.Elapsed.TotalMilliseconds, 1),
        ["problem"] = context.Items[RequestLog.ProblemKey] is string p ? $"/problems/v1/{p}"
          : failed ? "/problems/v1/internal"
          : status == 404 && endpoint is null ? "/problems/v1/route_not_found" // no route matched, so no filter set one
          : null,
      });
    }
  }
}
