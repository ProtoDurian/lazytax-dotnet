namespace LazyTax.Platform.Http;

/// <summary>
/// Built-in policy names (auth layer 2). More come from config <c>Auth:Policies</c> in nitroso's shape:
/// <c>{ Field: scope|roles, Type: Any|All, Target: [...] }</c>.
/// </summary>
public static class Policies
{
  /// <summary>No token needed.</summary>
  public const string Public = "public";

  /// <summary>Any valid token.</summary>
  public const string Registered = "registered";

  /// <summary><c>scope</c> contains <c>admin</c>.</summary>
  public const string Admin = "admin";
}
