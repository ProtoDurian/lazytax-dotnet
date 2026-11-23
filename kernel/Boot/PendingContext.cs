using System.Reflection;
using LazyTax.Platform.Context;
using LazyTax.Platform.Problems;

namespace LazyTax.Kernel.Boot;

/// <summary>
/// A context whose contract is merged but whose <c>src/Context.cs</c> is not yet. Its <c>provides</c> resolve to
/// placeholders, so contexts that need it boot and test their own code; any call into it answers 501.
/// The contract interface must be <c>Ctx.&lt;Name&gt;.Contract.V&lt;n&gt;.I&lt;Name&gt;V&lt;n&gt;</c>.
/// </summary>
public sealed class PendingContext(string name) : IContext
{
  public string Name { get; } = name;

  public string Pascal => ManifestLoader.Pascal(this.Name);

  /// <summary>The context's src assembly (it carries context.yaml), loaded by name, not scanned.</summary>
  public Assembly Assembly => Assembly.Load($"Ctx.{this.Pascal}");

  public void Register(ContextBuilder c)
  {
    foreach (var contract in c.Manifest.Provides)
    {
      var version = contract[(contract.IndexOf(".v", StringComparison.Ordinal) + 2)..];
      var typeName = $"Ctx.{this.Pascal}.Contract.V{version}.I{this.Pascal}V{version}";
      var type = Type.GetType($"{typeName}, Ctx.{this.Pascal}.Contract");
      if (type is null || !type.IsInterface)
        throw new ContextBootException([new ContextProblem(this.Name, c.Manifest.Folder,
          $"it has no src/Context.cs yet, and its contract {contract} has no interface {typeName}",
          "add src/Context.cs, or name the contract interface by the convention")]);
      c.ProvidePending(contract, type, () => PendingProxy.Create(type, this.Name, contract));
    }
  }
}

/// <summary>501: the context behind this contract is not built yet.</summary>
public sealed class ContextNotBuilt(string context, string contract, string method)
  : Problem(501, "context_not_built", "Context Not Built", $"{contract}.{method} is not available yet: context '{context}' has no src/Context.cs", new { context, contract });

/// <summary>Implements any contract interface; every call throws <see cref="ContextNotBuilt"/>.</summary>
public class PendingProxy : DispatchProxy
{
  private string context = string.Empty;
  private string contract = string.Empty;

  public static object Create(Type contractType, string context, string contract)
  {
    var proxy = DispatchProxy.Create(contractType, typeof(PendingProxy));
    var p = (PendingProxy)proxy;
    p.context = context;
    p.contract = contract;
    return proxy;
  }

  protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
    throw new ContextNotBuilt(this.context, this.contract, targetMethod?.Name ?? "?");
}
