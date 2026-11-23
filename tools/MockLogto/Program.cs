using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

// Mock Logto: a local OIDC issuer. No secrets: the RSA key is made at start and lives in memory.
//   GET  /oidc/.well-known/openid-configuration
//   GET  /oidc/jwks
//   POST /token  { "sub": "u1", "scope": "admin", "aud": "...", "expiresIn": 3600 }  ->  { "access_token": "..." }
// Run: dotnet run --project tools/MockLogto -- --urls http://localhost:55490
var app = WebApplication.CreateBuilder(args).Build();
var key = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "mock-logto-1" };
static string Issuer(HttpContext h) => $"{h.Request.Scheme}://{h.Request.Host}/oidc";
var audience = app.Configuration["Audience"] ?? "https://api.lazytax.local";

string[] algs = ["RS256"];

app.MapGet("/oidc/.well-known/openid-configuration", (HttpContext h) => Results.Json(new
{
  issuer = Issuer(h),
  jwks_uri = Issuer(h) + "/jwks",
  token_endpoint = $"{h.Request.Scheme}://{h.Request.Host}/token",
  id_token_signing_alg_values_supported = algs,
}));

app.MapGet("/oidc/jwks", () =>
{
  var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(key);
  return Results.Json(new { keys = new[] { new { kty = jwk.Kty, use = "sig", kid = jwk.Kid, alg = "RS256", n = jwk.N, e = jwk.E } } });
});

app.MapPost("/token", (HttpContext h, TokenRequest req) =>
{
  var claims = new Dictionary<string, object> { ["sub"] = req.Sub };
  if (!string.IsNullOrWhiteSpace(req.Scope)) claims["scope"] = req.Scope;
  // Extra claims (for example an email, to prove the app never logs it). They never override sub or scope.
  foreach (var (k, v) in req.Claims ?? []) claims.TryAdd(k, v.ToString());
  // A negative expiresIn mints an already expired token (for the "expired is 401" tests).
  var expires = DateTime.UtcNow.AddSeconds(req.ExpiresIn ?? 3600);
  var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
  {
    Issuer = Issuer(h),
    Audience = req.Aud ?? audience,
    Claims = claims,
    IssuedAt = expires.AddSeconds(-3600),
    NotBefore = expires.AddSeconds(-3600),
    Expires = expires,
    SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
  });
  return Results.Json(new { access_token = token, token_type = "Bearer", expires_in = req.ExpiresIn ?? 3600 });
});

app.Run();

internal sealed record TokenRequest(string Sub, string? Scope, string? Aud, int? ExpiresIn, Dictionary<string, JsonElement>? Claims);
