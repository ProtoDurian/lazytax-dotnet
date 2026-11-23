using Microsoft.AspNetCore.Authorization;

namespace LazyTax.Platform.Http;

/// <summary>
/// Auth layer 2, REQUIRED on every action (or its controller): the app refuses to start when an endpoint
/// has none. Use <see cref="Policies"/> names or a configured one.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class PolicyAttribute(string policy) : AuthorizeAttribute(policy)
{
  public string Name { get; } = policy;
}

/// <summary>
/// Auth layer 3: the route value <see cref="RouteParam"/> (default <c>userId</c>) must equal the token's
/// <c>sub</c>, unless the caller is admin. Otherwise 403. Runs before guards and the action.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class SubjectAttribute(string routeParam = "userId") : Attribute
{
  public string RouteParam { get; } = routeParam;
}

/// <summary>
/// Auth layer 5, declared: guards run in the order given, after layers 1-3 and before the action.
/// Order must follow <see cref="Guards.GuardStage"/> (plan, then debt); the app refuses to start otherwise.
/// The lock and "count, then write" (stage 3) stay inside the service's unit of work.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class GuardsAttribute(params Type[] guards) : Attribute
{
  public IReadOnlyList<Type> Guards { get; } = guards;
}
