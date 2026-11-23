using LazyTax.Platform.Data;

namespace Ctx.Proof.Contract.V1;

/// <summary>
/// proof.v1: proof plugins (manual, nfc, strava) and how a proof event becomes check evidence
/// (LazyTax Domain/Proof). Never rename or remove a member once merged.
/// </summary>
public interface IProofV1
{
  /// <summary>
  /// The one way in for every proof source (app call, webhook, poll): evaluates the event for each habit it
  /// can prove and reports each verdict to check as evidence. Runs inside the caller's unit of work.
  /// </summary>
  Task<IReadOnlyList<ProofOutcome>> Route(ProofEvent e, CancellationToken ct = default);

  /// <summary>The plugins this server offers (the Strava plugin only when Strava is enabled).</summary>
  IReadOnlyList<PluginManifest> ListPlugins();

  /// <summary>The habit's binding, or null when it only has the always-bound proofs. 404 when the habit is not the owner's.</summary>
  Task<ProofBindingInfo?> GetBinding(Owner owner, Guid habitId, CancellationToken ct = default);

  /// <summary>HabitId to bound plugin key, for the owner's habits that have a binding.</summary>
  Task<IReadOnlyDictionary<Guid, string>> ListPluginKeys(Owner owner, CancellationToken ct = default);

  /// <summary>Habit ids of the owner that have at least one NFC tag linked.</summary>
  Task<IReadOnlyList<Guid>> ListTaggedHabits(Owner owner, CancellationToken ct = default);
}

/// <summary>What a plugin says about itself. The app renders a binding form from <paramref name="SettingsSchema"/>.</summary>
/// <param name="SettingsSchema">JSON Schema as text.</param>
/// <param name="MinTier">null: every tier may bind it.</param>
/// <param name="AlwaysBound">Available on every habit without a binding (manual, nfc).</param>
public sealed record PluginManifest(
  string Key,
  string Name,
  int Version,
  string[] Platforms,
  string SettingsSchema,
  string? MinTier,
  bool AlwaysBound);

/// <summary>One proof per habit. A habit with no row reports the manual proof.</summary>
/// <param name="Settings">A JSON object as text; its shape is the plugin's settings schema.</param>
public sealed record ProofBindingInfo(Guid HabitId, string PluginKey, int PluginVersion, string Settings);

/// <summary>
/// Every source (app call, webhook, poll) builds one. <paramref name="HabitId"/> targets one habit (the Complete
/// button, an NFC tap); without it the router evaluates every habit of the user bound to the plugin (a Strava
/// activity). (habit, plugin, <paramref name="ExternalId"/>) is idempotent.
/// </summary>
/// <param name="PayloadJson">The plugin's own event body as JSON text.</param>
public sealed record ProofEvent(
  string PluginKey,
  string UserId,
  string ExternalId,
  DateTime OccurredAtUtc,
  Guid? HabitId = null,
  string? PayloadJson = null,
  string? Notes = null);

/// <summary>What routing one event did to one habit. ExecutionId is null when the plugin had no verdict or nothing changed.</summary>
public sealed record ProofOutcome(Guid HabitId, Guid? ExecutionId);

public static class ProofPlugins
{
  public const string Manual = "manual";
  public const string Nfc = "nfc";
  public const string Strava = "strava";
}
