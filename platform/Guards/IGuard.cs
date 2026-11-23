using LazyTax.Platform.Http;

namespace LazyTax.Platform.Guards;

/// <summary>The fixed order of business rules (DESIGN.md section 4, layer 5).</summary>
public enum GuardStage
{
  /// <summary>1. Cheap plan reads first; a "no" stops here (<c>plan.EnsureTier</c>, quota &gt; 0).</summary>
  Plan = 1,

  /// <summary>2. Debt gate (delete account).</summary>
  Debt = 2,
}

/// <summary>What a guard sees: the caller, the route (or MCP argument) values, and DI.</summary>
public sealed record GuardContext(Caller Caller, IReadOnlyDictionary<string, string?> Values, IServiceProvider Services)
{
  /// <summary>The target user: the subject value (<c>userId</c>) if present, else the caller.</summary>
  public string UserId => this.Values.TryGetValue("userId", out var u) && u is not null ? u : this.Caller.RequireSub();
}

/// <summary>
/// A layer-5 business rule declared on a route or tool with <c>[Guards(...)]</c>. Throw a <c>Problem</c> to
/// refuse. Register it with <c>c.Services.AddScoped&lt;MyGuard&gt;()</c>.
/// </summary>
public interface IGuard
{
  GuardStage Stage { get; }

  /// <summary>Short label for the <c>policies</c> table, e.g. <c>plan.quota(vacationWindows)</c>.</summary>
  string Label { get; }

  Task Check(GuardContext ctx, CancellationToken ct);
}
