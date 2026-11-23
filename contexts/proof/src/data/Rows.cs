using System.ComponentModel.DataAnnotations;

namespace Ctx.Proof.Data;

/// <summary>A habit's proof plugin and its settings (one per habit). UserId is a copy of the habit's owner.</summary>
public sealed class ProofBindingRow
{
  public Guid Id { get; set; }

  [MaxLength(128)]
  public required string UserId { get; set; }

  public required Guid HabitId { get; set; }

  [MaxLength(64)]
  public required string PluginKey { get; set; }

  public required int PluginVersion { get; set; }

  [MaxLength(4096)]
  public required string Settings { get; set; }

  public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>The tag id is embedded in the physical tag's URL; first come, first served.</summary>
public sealed class NfcTagRow
{
  [MaxLength(64)]
  public required string Id { get; set; }

  [MaxLength(128)]
  public required string UserId { get; set; }

  public required Guid HabitId { get; set; }

  public DateTime ClaimedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A user's Strava link. Tokens are encrypted. Deleted on disconnect, deauthorize and account purge (Strava's terms).</summary>
public sealed class StravaConnectionRow
{
  public Guid Id { get; set; }

  [MaxLength(128)]
  public required string UserId { get; set; }

  public required long AthleteId { get; set; }

  [MaxLength(2048)]
  public required string AccessToken { get; set; }

  [MaxLength(2048)]
  public required string RefreshToken { get; set; }

  public required DateTime ExpiresAtUtc { get; set; }

  [MaxLength(256)]
  public required string Scope { get; set; }

  /// <summary>The refresh claim: HTTP never runs inside a transaction, so what was a row lock is a short lease.</summary>
  public DateTime? RefreshLeaseUntilUtc { get; set; }

  public DateTime ConnectedAtUtc { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Webhook events waiting for the worker (IDs only, no Strava data). No user id: an event names an athlete.</summary>
public sealed class StravaEventRow
{
  public Guid Id { get; set; }

  [MaxLength(16)]
  public required string ObjectType { get; set; }

  [MaxLength(16)]
  public required string AspectType { get; set; }

  public required long ObjectId { get; set; }
  public required long OwnerId { get; set; }
  public required DateTime EventTimeUtc { get; set; }
  public required bool Deauthorized { get; set; }
  public int Attempts { get; set; }
  public DateTime? ClaimedUntilUtc { get; set; }
  public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}
