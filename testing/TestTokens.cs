using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LazyTax.Testing;

/// <summary>Mints tokens the test app accepts (in-process key; SIT uses tools/MockLogto instead).</summary>
public static class TestTokens
{
  public const string Issuer = "http://mock-logto.test/oidc";
  public const string Audience = "https://api.lazytax.local";

  public static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "test-key" };

  /// <summary>A token for <paramref name="sub"/> with space-joined <paramref name="scopes"/> (e.g. "admin").</summary>
  public static string For(string sub, params string[] scopes) => Mint(sub, scopes, Issuer, Audience, TimeSpan.FromHours(1));

  public static string Mint(string sub, string[] scopes, string issuer, string audience, TimeSpan lifetime)
  {
    var claims = new Dictionary<string, object> { ["sub"] = sub };
    if (scopes.Length > 0) claims["scope"] = string.Join(' ', scopes);
    var now = DateTime.UtcNow;
    return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
      Issuer = issuer,
      Audience = audience,
      Claims = claims,
      IssuedAt = now.AddMinutes(-1),
      NotBefore = now.AddMinutes(-1),
      Expires = now.Add(lifetime),
      SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256),
    });
  }

  /// <summary>An id token like Logto's, with the claims account reads on sign-up (username, email, email_verified).</summary>
  public static string IdToken(string sub, string username, string email, bool emailVerified = true)
  {
    var now = DateTime.UtcNow;
    return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
      Issuer = Issuer,
      Audience = "lazytax-app",
      Claims = new Dictionary<string, object> { ["sub"] = sub, ["username"] = username, ["email"] = email, ["email_verified"] = emailVerified },
      IssuedAt = now.AddMinutes(-1),
      NotBefore = now.AddMinutes(-1),
      Expires = now.AddHours(1),
      SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256),
    });
  }

  /// <summary>A new random user id (JWT sub). Use one per test so tests never share rows.</summary>
  public static string NewUser() => "u_" + Guid.NewGuid().ToString("N")[..12];

  /// <summary>Sets the bearer token on this client and returns it.</summary>
  public static HttpClient As(this HttpClient client, string sub, params string[] scopes)
  {
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", For(sub, scopes));
    return client;
  }
}
