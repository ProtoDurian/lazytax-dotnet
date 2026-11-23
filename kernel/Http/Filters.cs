using LazyTax.Kernel.Logging;
using LazyTax.Platform.Guards;
using LazyTax.Platform.Http;
using LazyTax.Platform.Problems;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LazyTax.Kernel.Http;

public sealed class HttpCallerAccessor(IHttpContextAccessor http) : ICallerAccessor
{
  private Caller? overridden;

  public Caller Caller => this.overridden ?? ClaimsFields.ToCaller(http.HttpContext?.User);

  /// <summary>MCP and workers set the caller explicitly.</summary>
  public void Set(Caller caller) => this.overridden = caller;
}

/// <summary>Layer 3 (subject) then layer 5 (declared guards), in that order, before the action.</summary>
public sealed class AuthLayersFilter : IAsyncActionFilter
{
  public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
  {
    var meta = context.ActionDescriptor.EndpointMetadata;
    var caller = ClaimsFields.ToCaller(context.HttpContext.User);
    var values = context.RouteData.Values.ToDictionary(k => k.Key, v => v.Value?.ToString());

    if (meta.OfType<SubjectAttribute>().LastOrDefault() is { } subject)
      SubjectRule.Check(caller, values.GetValueOrDefault(subject.RouteParam));

    if (meta.OfType<GuardsAttribute>().LastOrDefault() is { } guards)
      await GuardRunner.Run(guards.Guards, new GuardContext(caller, values, context.HttpContext.RequestServices), context.HttpContext.RequestAborted);

    await next();
  }
}

public static class SubjectRule
{
  /// <summary>The target user must be the caller, unless the caller is admin (LazyTax's GuardOrAny).</summary>
  public static void Check(Caller caller, string? target)
  {
    if (caller.IsAdmin) return;
    if (caller.Sub is null) throw new Unauthenticated();
    if (target is null || target != caller.Sub) throw new Forbidden();
  }
}

public static class GuardRunner
{
  public static async Task Run(IEnumerable<Type> guards, GuardContext ctx, CancellationToken ct)
  {
    foreach (var t in guards)
    {
      var guard = (IGuard)ctx.Services.GetRequiredService(t);
      await guard.Check(ctx, ct);
    }
  }
}

/// <summary>Maps every thrown <see cref="Problem"/> to problem details (LazyTax's BaseController.MapException).</summary>
public sealed class ProblemExceptionFilter : IExceptionFilter
{
  public void OnException(ExceptionContext context)
  {
    if (context.Exception is not Problem p) return;
    context.HttpContext.Items[RequestLog.ProblemKey] = p.Id;
    context.Result = new ObjectResult(ProblemWriter.ToBody(p, context.HttpContext.TraceIdentifier))
    {
      StatusCode = p.Status,
      ContentTypes = { "application/problem+json" },
    };
    context.ExceptionHandled = true;
  }
}

/// <summary>Prepends each context's http.prefix to its controllers' routes.</summary>
public sealed class ContextRoutePrefixConvention(IReadOnlyDictionary<System.Reflection.Assembly, string> prefixes)
  : IApplicationModelConvention
{
  public void Apply(ApplicationModel application)
  {
    foreach (var controller in application.Controllers)
    {
      if (!prefixes.TryGetValue(controller.ControllerType.Assembly, out var prefix)) continue;
      var model = new AttributeRouteModel(new RouteAttribute(prefix.TrimStart('/')));
      foreach (var selector in controller.Selectors)
        selector.AttributeRouteModel = selector.AttributeRouteModel is null
          ? model
          : AttributeRouteModel.CombineAttributeRouteModel(model, selector.AttributeRouteModel);
      if (controller.Selectors.Count == 0) controller.Selectors.Add(new SelectorModel { AttributeRouteModel = model });
    }
  }
}
