using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ctx.Proof.Domain;

/// <summary>
/// The OAuth state for the connect flow: which user started it and until when, HMAC-signed so the anonymous
/// callback can't be pointed at another user's account.
/// </summary>
public static class StravaState
{
  public static string Sign(string userId, DateTime expiresAtUtc, string key)
  {
    var payload = $"{new DateTimeOffset(expiresAtUtc).ToUnixTimeSeconds()}:{userId}";
    var body = B64(Encoding.UTF8.GetBytes(payload));
    return $"{body}.{Mac(body, key)}";
  }

  /// <summary>The user id, or null when the state is forged, malformed or expired.</summary>
  public static string? Verify(string? state, DateTime nowUtc, string key)
  {
    var parts = state?.Split('.');
    if (parts is not [var body, var mac]) return null;
    if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Mac(body, key)), Encoding.ASCII.GetBytes(mac))) return null;

    string payload;
    try
    {
      payload = Encoding.UTF8.GetString(FromB64(body));
    }
    catch (FormatException)
    {
      return null;
    }

    var sep = payload.IndexOf(':');
    if (sep <= 0 || !long.TryParse(payload[..sep], out var expires)) return null;
    if (DateTimeOffset.FromUnixTimeSeconds(expires).UtcDateTime < nowUtc) return null;
    var userId = payload[(sep + 1)..];
    return userId.Length == 0 ? null : userId;
  }

  private static string Mac(string body, string key) => B64(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(body)));

  private static string B64(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

  private static byte[] FromB64(string s)
  {
    var t = s.Replace('-', '+').Replace('_', '/');
    return Convert.FromBase64String(t.PadRight(t.Length + ((4 - (t.Length % 4)) % 4), '='));
  }
}

/// <summary>
/// The signed webhook: <c>X-Lazytax-Signature</c> is the hex HMAC-SHA256 of <c>&lt;timestamp&gt;.&lt;raw body&gt;</c>
/// and <c>X-Lazytax-Timestamp</c> the unix seconds. Constant-time compare; the timestamp must be within the
/// tolerance of now (a replay or a future stamp is refused).
/// </summary>
public static class WebhookSignature
{
  public static string Sign(string secret, string timestamp, string rawBody) =>
    Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{rawBody}"))).ToLowerInvariant();

  public static bool Verify(string secret, string? timestamp, string? signature, string rawBody, DateTime nowUtc, int toleranceSec)
  {
    if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(signature) || !long.TryParse(timestamp, out var ts)) return false;
    var skew = nowUtc - DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
    if (skew > TimeSpan.FromSeconds(toleranceSec) || skew < -TimeSpan.FromSeconds(toleranceSec)) return false;
    var expected = Encoding.ASCII.GetBytes(Sign(secret, timestamp, rawBody));
    return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(signature.ToLowerInvariant()));
  }
}

public static class StravaWebhookParser
{
  private static readonly long MinUnixSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();
  private static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

  /// <summary>Null for a malformed event or another subscription's (subscriptionId 0 skips that check).</summary>
  public static StravaWebhookEvent? Parse(JsonElement body, long subscriptionId)
  {
    if (body.ValueKind != JsonValueKind.Object) return null;
    if (!body.TryGetProperty("object_type", out var objectType) || objectType.ValueKind != JsonValueKind.String) return null;
    if (!body.TryGetProperty("aspect_type", out var aspectType) || aspectType.ValueKind != JsonValueKind.String) return null;
    if (!TryGetInt64(body, "object_id", out var oid)) return null;
    if (!TryGetInt64(body, "owner_id", out var owner)) return null;
    if (!TryGetInt64(body, "event_time", out var at) || at < MinUnixSeconds || at > MaxUnixSeconds) return null;
    if (subscriptionId != 0 && (!TryGetInt64(body, "subscription_id", out var sid) || sid != subscriptionId)) return null;

    var deauthorized = body.TryGetProperty("updates", out var updates) && updates.ValueKind == JsonValueKind.Object
                       && updates.TryGetProperty("authorized", out var authorized)
                       && authorized.ValueKind == JsonValueKind.String && authorized.GetString() == "false";
    var type = objectType.GetString()!;
    var aspect = aspectType.GetString()!;
    if (type is not ("activity" or "athlete") || aspect is not ("create" or "update" or "delete")) return null;
    return new StravaWebhookEvent(type, aspect, oid, owner, DateTimeOffset.FromUnixTimeSeconds(at).UtcDateTime, type == "athlete" && deauthorized);
  }

  // A JSON number that fits a long; TryGetInt64 throws on any other kind, such as a string.
  private static bool TryGetInt64(JsonElement body, string name, out long value)
  {
    value = 0;
    return body.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out value);
  }
}
