using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace LazyTax.Platform.Http;

/// <summary>
/// Base for every context controller. Errors are thrown as <c>Problem</c>s; the kernel maps them.
/// Routes are relative to the context's <c>http.prefix</c> (the kernel prepends it), so a controller
/// writes <c>[Route("")]</c> or <c>[HttpGet("{userId}")]</c>, never the full path.
/// </summary>
[ApiController]
public abstract class ContextController : ControllerBase
{
  /// <summary>The validated caller (sub, admin, scopes).</summary>
  protected Caller Caller => this.HttpContext.RequestServices.GetRequiredService<ICallerAccessor>().Caller;
}

/// <summary>The current caller, for services that need it (HTTP request or MCP call).</summary>
public interface ICallerAccessor
{
  Caller Caller { get; }
}
