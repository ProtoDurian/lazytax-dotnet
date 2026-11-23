namespace Ctx.Proof.Domain;

public sealed class ProofOptions
{
  public StravaOptions Strava { get; set; } = new();
}

/// <summary>Config <c>Contexts:proof:Strava</c>. The defaults are development values: set your own.</summary>
public sealed class StravaOptions
{
  /// <summary>Off: the strava plugin isn't offered, the endpoints return 404 and the worker doesn't run.</summary>
  public bool Enabled { get; set; }

  public string ClientId { get; set; } = "dev-client-id";

  /// <summary>Also signs the connect flow's state.</summary>
  public string ClientSecret { get; set; } = "dev-client-secret-change-me";

  /// <summary>Our own token, echoed by Strava when the webhook subscription is created.</summary>
  public string VerifyToken { get; set; } = "dev-verify-token";

  /// <summary>HMAC key for the signed webhook (a signing proxy in front of the endpoint).</summary>
  public string WebhookSecret { get; set; } = "dev-webhook-secret-change-me";

  public int WebhookToleranceSec { get; set; } = 300;

  /// <summary>Key material for tokens at rest (AES-256-GCM, SHA-256 of this string).</summary>
  public string EncryptionKey { get; set; } = "dev-encryption-key-change-me";

  /// <summary>The subscription id Strava returned; other ids are ignored. 0 = not checked.</summary>
  public long SubscriptionId { get; set; }

  public string CallbackUrl { get; set; } = "http://localhost:5000/api/v1/Strava/callback";

  public string AppRedirectUri { get; set; } = "lazytax://strava";

  public int MaxAttempts { get; set; } = 5;

  public int PollSeconds { get; set; } = 20;

  public int StateLifetimeMinutes { get; set; } = 15;
}
