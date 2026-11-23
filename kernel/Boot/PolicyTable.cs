using LazyTax.Platform.Guards;
using LazyTax.Platform.Http;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace LazyTax.Kernel.Boot;

public sealed record EndpointRow(string Context, string Kind, string Method, string Path, string? Policy, string? Subject, IReadOnlyList<Type> GuardTypes, string Guards);

/// <summary>Every route and MCP tool with its method, path, policy, subject and guards (<c>Kernel policies</c>).</summary>
public static class PolicyTable
{
  /// <param name="sources">The app's own route sources (<c>((IEndpointRouteBuilder)app).DataSources</c>); before the app
  /// starts, the DI <see cref="EndpointDataSource"/> is still empty. Null means the started app's DI one.</param>
  public static List<EndpointRow> Endpoints(IServiceProvider services, IEnumerable<EndpointDataSource>? sources = null)
  {
    var model = services.GetRequiredService<KernelModel>();
    using var scope = services.CreateScope();
    var rows = new List<EndpointRow>();
    foreach (var e in (sources?.SelectMany(s => s.Endpoints) ?? services.GetRequiredService<EndpointDataSource>().Endpoints).OfType<RouteEndpoint>())
    {
      var action = e.Metadata.GetMetadata<ControllerActionDescriptor>();
      var ctx = action is not null ? model.ContextByAssembly.GetValueOrDefault(action.ControllerTypeInfo.Assembly) ?? "kernel" : "kernel";
      var methods = e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? ["ANY"];
      var policy = e.Metadata.OfType<PolicyAttribute>().LastOrDefault()?.Name;
      var subject = e.Metadata.OfType<SubjectAttribute>().LastOrDefault()?.RouteParam;
      var guards = e.Metadata.OfType<GuardsAttribute>().LastOrDefault()?.Guards ?? [];
      rows.Add(new EndpointRow(ctx, "http", string.Join(",", methods), "/" + e.RoutePattern.RawText?.TrimStart('/'),
        policy, subject, guards, Labels(guards, scope.ServiceProvider)));
    }
    foreach (var t in model.Tools)
      rows.Add(new EndpointRow(t.Context, "mcp", "TOOL", t.Tool.Name, t.Tool.Policy, t.Tool.Subject, t.Tool.Guards,
        Labels(t.Tool.Guards, scope.ServiceProvider)));
    return [.. rows.OrderBy(r => r.Context).ThenBy(r => r.Kind).ThenBy(r => r.Path)];
  }

  public static string Render(IEnumerable<EndpointRow> rows)
  {
    var list = rows.ToList();
    string[] head = ["CONTEXT", "METHOD", "PATH / TOOL", "POLICY", "SUBJECT", "GUARDS"];
    var cells = list.Select(r => new[] { r.Context, r.Method, r.Path, r.Policy ?? "(NONE)", r.Subject ?? "-", r.Guards.Length > 0 ? r.Guards : "-" }).ToList();
    var widths = head.Select((h, i) => Math.Max(h.Length, cells.Count == 0 ? 0 : cells.Max(c => c[i].Length))).ToArray();
    string Line(string[] c) => string.Join("  ", c.Select((v, i) => v.PadRight(widths[i]))).TrimEnd();
    return string.Join('\n', new[] { Line(head) }.Concat(cells.Select(Line)));
  }

  private static string Labels(IReadOnlyList<Type> guards, IServiceProvider sp) =>
    string.Join(" > ", guards.Select(t => sp.GetService(t) is IGuard g ? g.Label : t.Name + "(unregistered)"));
}
