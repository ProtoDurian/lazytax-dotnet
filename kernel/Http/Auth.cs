using LazyTax.Kernel.Logging;
using System.Security.Claims;
using System.Text.Json;
using LazyTax.Platform.Http;
using LazyTax.Platform.Problems;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace LazyTax.Kernel.Http;

/// <summary>nitroso's policy shape: <c>{ Field: scope|roles, Type: Any|All, Target: [...] }</c>.</summary>
public sealed class PolicyOption
{
  public string Field { get; set; } = "scope";
  public string Type { get; set; } = "All";
  public string[] Target { get; set; } = [];
}

public sealed class AuthOptions
{
  /// <summary>Pinned issuer, e.g. http://localhost:55490/oidc. Discovery and JWKS come from here.</summary>
  public string Issuer { get; set; } = string.Empty;

  /// <summary>Pinned audience.</summary>
  public string Audience { get; set; } = string.Empty;

  /// <summary>Optional explicit JWKS URL; defaults to OIDC discovery under the issuer.</summary>
  public string? JwksUrl { get; set; }

  public Dictionary<string, PolicyOption> Policies { get; set; } = [];
}

/// <summary>Reads scopes/roles from the validated token (LazyTax's AuthHelper.FieldToScope).</summary>
public static class ClaimsFields
{
  public static IEnumerable<string> Values(ClaimsPrincipal? user, string field)
  {
    var values = user?.FindAll(field).Select(c => c.Value) ?? [];
    return field == "scope" ? values.SelectMany(v => v.Split(' ', StringSplitOptions.RemoveEmptyEntries)) : values;
  }

  public static Caller ToCaller(ClaimsPrincipal? user)
  {
    if (user?.Identity?.IsAuthenticated != true) return Caller.Anonymous;
    var scopes = Values(user, "scope").ToHashSet();
    return new Caller(user.FindFirst("sub")?.Value, scopes.Contains("admin"), scopes);
  }
}

/// <summary>LazyTax's HasAny / HasAll requirements, one class.</summary>
public sealed record ScopeRequirement(string Field, bool All, IReadOnlyList<string> Target) : IAuthorizationRequirement;

public sealed class ScopeRequirementHandler : AuthorizationHandler<ScopeRequirement>
{
  protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScopeRequirement req)
  {
    var have = ClaimsFields.Values(context.User, req.Field).ToHashSet();
    var ok = req.All ? req.Target.All(have.Contains) : req.Target.Any(have.Contains);
    if (ok) context.Succeed(req);
    return Task.CompletedTask;
  }
}

/// <summary>401 and 403 as problem details, like every other error.</summary>
public sealed class ProblemAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
  private readonly AuthorizationMiddlewareResultHandler inner = new();

  public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
  {
    if (result.Challenged) return ProblemWriter.Write(context, new Unauthenticated());
    if (result.Forbidden) return ProblemWriter.Write(context, new Forbidden());
    return this.inner.HandleAsync(next, context, policy, result);
  }
}

public static class AuthSetup
{
  public static IServiceCollection AddKernelAuth(this IServiceCollection services)
  {
    // Options are read when first used (not at registration), so tests can override configuration.
    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
    services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<IConfiguration>((j, cfg) =>
    {
      var o = Read(cfg);
      // Layer 1: signature from JWKS, issuer and audience pinned, no clock skew (LazyTax's settings).
      j.MapInboundClaims = false;
      j.Authority = o.Issuer;
      j.Audience = o.Audience;
      j.RequireHttpsMetadata = o.Issuer.StartsWith("https://", StringComparison.Ordinal);
      j.TokenValidationParameters.ValidIssuer = o.Issuer;
      j.TokenValidationParameters.ValidAudience = o.Audience;
      j.TokenValidationParameters.ValidateIssuer = true;
      j.TokenValidationParameters.ValidateAudience = true;
      j.TokenValidationParameters.ValidateLifetime = true;
      j.TokenValidationParameters.ValidateIssuerSigningKey = true;
      j.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
      j.TokenValidationParameters.NameClaimType = "sub";
      if (o.JwksUrl is { } jwks) j.MetadataAddress = jwks;
    });

    services.AddSingleton<IAuthorizationHandler, ScopeRequirementHandler>();
    services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();
    services.AddAuthorization();
    services.AddOptions<AuthorizationOptions>().Configure<IConfiguration>((a, cfg) =>
    {
      var o = Read(cfg);
      // Layer 2 built-ins. "public" passes everyone, but the route must still say so.
      a.AddPolicy(Policies.Public, p => p.RequireAssertion(_ => true));
      a.AddPolicy(Policies.Registered, p => p.RequireAuthenticatedUser());
      a.AddPolicy(Policies.Admin, p => p.RequireAuthenticatedUser().AddRequirements(new ScopeRequirement("scope", true, ["admin"])));
      foreach (var (name, v) in o.Policies)
      {
        if (v.Type is not ("Any" or "All")) throw new InvalidOperationException($"Auth policy {name}: Type must be Any or All");
        if (v.Field is not ("scope" or "roles")) throw new InvalidOperationException($"Auth policy {name}: Field must be scope or roles");
        a.AddPolicy(name, p => p.RequireAuthenticatedUser().AddRequirements(new ScopeRequirement(v.Field, v.Type == "All", v.Target)));
      }
    });
    return services;
  }

  private static AuthOptions Read(IConfiguration cfg) => cfg.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
}

public static class ProblemWriter
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public static async Task Write(HttpContext context, Problem p)
  {
    context.Items[RequestLog.ProblemKey] = p.Id;
    context.Response.StatusCode = p.Status;
    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsync(JsonSerializer.Serialize(ToBody(p, context.TraceIdentifier), Json));
  }

  public static Dictionary<string, object?> ToBody(Problem p, string traceId) => new()
  {
    ["type"] = $"/problems/v1/{p.Id}",
    ["title"] = p.Title,
    ["status"] = p.Status,
    ["detail"] = p.Message,
    ["data"] = p.Data,
    ["traceId"] = traceId,
  };
}
