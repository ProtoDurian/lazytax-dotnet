namespace LazyTax.Kernel.Boot;

/// <summary>One broken context. The app prints every one and stops before it listens.</summary>
public sealed record ContextProblem(string Context, string Folder, string Problem, string Fix)
{
  public override string ToString() => $"context '{this.Context}' ({this.Folder}): {this.Problem}. Fix: {this.Fix}";
}

public sealed class ContextBootException(IReadOnlyList<ContextProblem> problems)
  : Exception("The app refused to start:\n  - " + string.Join("\n  - ", problems))
{
  public IReadOnlyList<ContextProblem> Problems { get; } = problems;
}
