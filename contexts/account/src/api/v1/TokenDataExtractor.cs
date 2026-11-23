using Ctx.Account.Domain;
using LazyTax.Platform.Problems;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Ctx.Account.Api.V1;

/// <summary>400: an id/access token pair that does not validate or lacks a field (LazyTax InvalidUserToken).</summary>
public sealed class InvalidUserToken(string detail, string token, string[] fields)
  : Problem(400, "invalid_user_token", "Invalid User Token", detail, new { token, fields });

/// <summary>
/// LazyTax's TokenDataExtractor: validates the access token and id token with the app's own JWT settings
/// (same issuer and keys; the id token's audience is not checked) and reads username, email and scopes.
/// </summary>
public sealed class TokenDataExtractor(IOptionsMonitor<JwtBearerOptions> jwt)
{
  public async Task<UserRecord> Extract(string sub, string idToken, string accessToken, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(idToken)) throw new InvalidUserToken("Missing IdToken", "ID", []);
    if (string.IsNullOrWhiteSpace(accessToken)) throw new InvalidUserToken("Missing AccessToken", "Access", []);
    var opt = jwt.Get(JwtBearerDefaults.AuthenticationScheme);
    var tvp = opt.TokenValidationParameters.Clone();
    // A configured document (tests) wins over fetching discovery (production).
    var cfg = opt.Configuration ?? (opt.ConfigurationManager is { } cm ? await cm.GetConfigurationAsync(ct) : null);
    if (cfg is not null) tvp.IssuerSigningKeys = cfg.SigningKeys;

    var handler = opt.TokenHandlers[0];
    var at = await handler.ValidateTokenAsync(accessToken, tvp);
    if (!at.IsValid) throw new InvalidUserToken($"Token validation failed: {at.Exception?.Message}", "Access", []);
    var itp = tvp.Clone();
    itp.ValidateAudience = false;
    var it = await handler.ValidateTokenAsync(idToken, itp);
    if (!it.IsValid) throw new InvalidUserToken($"Token validation failed: {it.Exception?.Message}", "ID", []);

    static string? Claim(TokenValidationResult r, string type) => r.ClaimsIdentity.FindFirst(type)?.Value;
    var missing = new List<string>();
    var idSub = Claim(it, "sub");
    var atSub = Claim(at, "sub");
    var username = Claim(it, "username");
    var email = Claim(it, "email");
    var verified = Claim(it, "email_verified");
    if (idSub is null) missing.Add("sub (ID token)");
    if (atSub is null) missing.Add("sub (access token)");
    if (email is null) missing.Add("email");
    if (verified is null) missing.Add("email_verified");
    if (username is null) missing.Add("username");
    if (missing.Count > 0) throw new InvalidUserToken("Fields missing", "ID", [.. missing]);
    if (idSub != atSub) throw new InvalidUserToken("ID and Access Tokens subject mismatch", "ID", []);
    if (idSub != sub) throw new InvalidUserToken("Sub of tokens do not match auth token", "ID/Access", []);

    var emailVerified = verified is "true" or "True";
    var scopes = Claim(at, "scope")?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
    return new UserRecord(username!, email!, emailVerified, emailVerified && username!.Length > 0 && email!.Length > 0, scopes);
  }
}
