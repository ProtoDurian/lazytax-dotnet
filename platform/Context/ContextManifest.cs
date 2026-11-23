namespace LazyTax.Platform.Context;

/// <summary>The parsed <c>context.yaml</c>. The kernel validates it before the app listens.</summary>
public sealed record ContextManifest
{
  /// <summary>Random, never changes (e.g. <c>ctx_7f3k2q</c>).</summary>
  public required string Id { get; init; }

  /// <summary>Folder name: lowercase letters and digits (e.g. <c>vacation</c>). Also the DB schema.</summary>
  public required string Name { get; init; }

  /// <summary>Contracts this context calls (e.g. <c>plan.v1</c>). Must be provided by another context.</summary>
  public IReadOnlyList<string> Needs { get; init; } = [];

  /// <summary>Contracts this context implements (e.g. <c>vacation.v1</c>).</summary>
  public IReadOnlyList<string> Provides { get; init; } = [];

  /// <summary>Infrastructure from <c>dependency.yaml</c> (e.g. <c>postgres.main</c>).</summary>
  public IReadOnlyList<string> Uses { get; init; } = [];

  /// <summary>Route prefix of its controllers, e.g. <c>/api/v1/vacation</c>.</summary>
  public string? HttpPrefix { get; init; }

  /// <summary>MCP tool prefix, e.g. <c>vacation</c>; tools are named <c>vacation.&lt;verb&gt;</c>.</summary>
  public string? McpPrefix { get; init; }

  /// <summary>Repo-relative folder, e.g. <c>contexts/vacation</c> (set by the build, used in errors).</summary>
  public string Folder { get; init; } = string.Empty;
}
