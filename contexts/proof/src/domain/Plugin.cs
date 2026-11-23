using System.Text.Json;
using System.Text.Json.Serialization;
using Ctx.Check.Contract.V1;
using Ctx.Proof.Contract.V1;
using LazyTax.Platform.Problems;

namespace Ctx.Proof.Domain;

/// <summary>A habit's binding as a plugin sees it. Settings are the normalized JSON stored at bind time.</summary>
public sealed record BoundProof(Guid Id, Guid HabitId, string UserId, string PluginKey, int PluginVersion, string Settings);

/// <summary>The Pending check the event falls in, so a plugin can re-read the whole day from its source.</summary>
public sealed record ProofWindow(DateOnly LocalDate, string Timezone, DateTime OpensAtUtc, DateTime DeadlineUtc)
{
  public static ProofWindow Of(DailyCheck c) => new(c.LocalDate, c.Timezone, c.OpensAtUtc, c.DeadlineUtc);
}

/// <summary>A plugin's decision for one check. Data is a summary serialized into the evidence.</summary>
public sealed record ProofVerdict(EvidenceVerdict Verdict, object? Data = null);

/// <summary>What the core, the registry and the router see. Settings and payloads stay JSON here.</summary>
public interface IProofPlugin
{
  PluginManifest Manifest { get; }

  /// <summary>Parses and checks a binding's settings; returns them normalized for storage, or throws <see cref="ValidationFailed"/>.</summary>
  string ValidateSettings(string settings);

  Task OnBind(BoundProof binding, CancellationToken ct);

  Task OnUnbind(BoundProof binding, CancellationToken ct);

  /// <summary>
  /// <paramref name="binding"/> is null when an always-bound plugin proves a habit it isn't bound to. A null
  /// verdict means the event says nothing about this check.
  /// </summary>
  Task<ProofVerdict?> Evaluate(BoundProof? binding, ProofEvent e, ProofWindow window, CancellationToken ct);
}

/// <summary>Settings or payload of a plugin that has none.</summary>
public sealed class Empty;

/// <summary>
/// What a plugin author extends. Stored settings and the event payload arrive as the plugin's own types, so a
/// plugin writes <c>Evaluate(TSettings, TEvent, ...)</c> and never touches JSON.
/// </summary>
public abstract class ProofPlugin<TSettings, TEvent> : IProofPlugin
  where TSettings : class, new()
  where TEvent : class, new()
{
  // Settings are user input: unknown fields are rejected so a typo doesn't silently do nothing.
  private static readonly JsonSerializerOptions SettingsJson = new(JsonSerializerDefaults.Web)
  {
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
  };

  // Payloads come from other systems (Strava): ignore fields the plugin doesn't model.
  private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

  public abstract PluginManifest Manifest { get; }

  /// <summary>Rules beyond the settings' shape, as field to messages. Empty when valid.</summary>
  protected virtual Dictionary<string, string[]> Validate(TSettings settings) => [];

  protected virtual Task OnBind(BoundProof binding, TSettings settings, CancellationToken ct) => Task.CompletedTask;

  protected virtual Task OnUnbind(BoundProof binding, TSettings settings, CancellationToken ct) => Task.CompletedTask;

  protected abstract Task<ProofVerdict?> Evaluate(TSettings settings, TEvent payload, ProofEvent e, ProofWindow window, CancellationToken ct);

  public string ValidateSettings(string settings)
  {
    TSettings? parsed;
    try
    {
      parsed = JsonSerializer.Deserialize<TSettings>(settings, SettingsJson);
    }
    catch (JsonException e)
    {
      throw new ValidationFailed("Invalid proof binding", "settings", e.Message);
    }

    if (parsed is null) throw new ValidationFailed("Invalid proof binding", "settings", "Settings must be a JSON object");
    var errors = this.Validate(parsed);
    if (errors.Count > 0) throw new ValidationFailed("Invalid proof binding", errors);
    return JsonSerializer.Serialize(parsed, SettingsJson);
  }

  public Task OnBind(BoundProof binding, CancellationToken ct) => this.OnBind(binding, SettingsOf(binding), ct);

  public Task OnUnbind(BoundProof binding, CancellationToken ct) => this.OnUnbind(binding, SettingsOf(binding), ct);

  public Task<ProofVerdict?> Evaluate(BoundProof? binding, ProofEvent e, ProofWindow window, CancellationToken ct)
  {
    TEvent? payload;
    try
    {
      payload = e.PayloadJson is null ? new TEvent() : JsonSerializer.Deserialize<TEvent>(e.PayloadJson, PayloadJson);
    }
    catch (JsonException)
    {
      payload = null;
    }

    return this.Evaluate(binding is null ? new TSettings() : SettingsOf(binding), payload ?? new TEvent(), e, window, ct);
  }

  // Stored settings were normalized by ValidateSettings when bound.
  private static TSettings SettingsOf(BoundProof binding) =>
    JsonSerializer.Deserialize<TSettings>(binding.Settings, PayloadJson) ?? new TSettings();
}

public sealed class PluginRegistry(IEnumerable<IProofPlugin> plugins, ProofOptions options)
{
  // Duplicate keys throw here, at startup, rather than routing to an arbitrary plugin.
  private readonly Dictionary<string, IProofPlugin> byKey = plugins.ToDictionary(p => p.Manifest.Key);

  /// <summary>Offered plugins; strava only when Strava is enabled (LazyTax left the plugin out when off).</summary>
  public IReadOnlyList<PluginManifest> List() =>
    [.. this.byKey.Values.Where(this.Offered).Select(p => p.Manifest).OrderBy(m => m.Key, StringComparer.Ordinal)];

  public IProofPlugin? Get(string key) => this.byKey.TryGetValue(key, out var p) && this.Offered(p) ? p : null;

  private bool Offered(IProofPlugin p) => p.Manifest.Key != ProofPlugins.Strava || options.Strava.Enabled;
}
