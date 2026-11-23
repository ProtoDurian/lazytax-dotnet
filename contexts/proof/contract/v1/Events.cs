namespace Ctx.Proof.Contract.V1;

/// <summary>Outbox event names emitted by proof.</summary>
public static class ProofEvents
{
  /// <summary>
  /// An activity (or its evidence) is gone at the source (Strava delete, disconnect, deauthorize). check
  /// removes the evidence row; the day it proved stays done. Payload <see cref="EvidenceRevoked"/>.
  /// </summary>
  public const string EvidenceRevoked = "proof.evidence_revoked";
}

/// <param name="ExternalId">null = all evidence of that proof for the user.</param>
public sealed record EvidenceRevoked(string UserId, string ProofKey, string? ExternalId);
