namespace LazyTax.Platform.Http;

/// <summary>Who is calling, from the validated JWT. The same for HTTP routes and MCP tools.</summary>
public sealed record Caller(string? Sub, bool IsAdmin, IReadOnlySet<string> Scopes)
{
  public static readonly Caller Anonymous = new(null, false, new HashSet<string>());

  public bool IsAuthenticated => this.Sub is not null;

  /// <summary>The caller's sub, or 401 when there is none.</summary>
  public string RequireSub() => this.Sub ?? throw new Problems.Unauthenticated();
}
