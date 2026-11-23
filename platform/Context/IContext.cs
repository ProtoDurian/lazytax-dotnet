namespace LazyTax.Platform.Context;

/// <summary>
/// The one registration of a context. Each context has exactly one, in <c>src/Context.cs</c>, named
/// <c>Ctx.&lt;Name&gt;.&lt;Name&gt;Context</c>. The kernel finds it at build time (no reflection scan) and calls
/// <see cref="Register"/> once, in <c>needs</c> order.
/// </summary>
public interface IContext
{
  void Register(ContextBuilder c);
}
