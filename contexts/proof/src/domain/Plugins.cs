using System.Text.Json;
using Ctx.Check.Contract.V1;
using Ctx.Proof.Contract.V1;

namespace Ctx.Proof.Domain;

/// <summary>The Complete button (app, widget). Always available on every habit, even one bound to another proof, and always passes.</summary>
public sealed class ManualProofPlugin : ProofPlugin<Empty, Empty>
{
  public override PluginManifest Manifest { get; } = new(
    ProofPlugins.Manual, "Check in", 1, ["ios", "android"],
    """{"type":"object","properties":{},"additionalProperties":false}""", null, true);

  protected override Task<ProofVerdict?> Evaluate(Empty settings, Empty payload, ProofEvent e, ProofWindow window, CancellationToken ct) =>
    Task.FromResult<ProofVerdict?>(new ProofVerdict(EvidenceVerdict.Passed));
}

public sealed class NfcPayload
{
  public string TagId { get; set; } = "";
}

/// <summary>A tap on a linked tag proves the habit. Always available; the event carries the tag id and targets one habit.</summary>
public sealed class NfcProofPlugin(NfcTagLookup tags) : ProofPlugin<Empty, NfcPayload>
{
  public override PluginManifest Manifest { get; } = new(
    ProofPlugins.Nfc, "NFC tap", 1, ["ios", "android"],
    """{"type":"object","properties":{},"additionalProperties":false}""", null, true);

  protected override async Task<ProofVerdict?> Evaluate(Empty settings, NfcPayload payload, ProofEvent e, ProofWindow window, CancellationToken ct)
  {
    if (e.HabitId is not { } habitId || payload.TagId.Length == 0) return null;
    return await tags.IsLinked(e.UserId, payload.TagId, habitId, ct) ? new ProofVerdict(EvidenceVerdict.Passed) : null;
  }
}

/// <summary>Breaks the nfc plugin to NfcTagService cycle: the plugin only needs the tag lookup.</summary>
public sealed class NfcTagLookup(INfcTagRepository repo)
{
  public async Task<bool> IsLinked(string userId, string tagId, Guid habitId, CancellationToken ct) =>
    (await repo.Get(LazyTax.Platform.Data.Owner.User(userId), tagId, ct))?.HabitId == habitId;
}

public sealed class StravaSettings
{
  public string[] SportTypes { get; set; } = [];
}

/// <summary>
/// Completes a habit on a recorded Strava activity of a bound sport type, any length. Activities typed in by
/// hand (manual) never count. Evidence carries the activity id only, no Strava data.
/// </summary>
public sealed class StravaProofPlugin(IStravaConnections connections) : ProofPlugin<StravaSettings, StravaActivity>
{
  // Strava's SportType values (developers.strava.com/docs/reference, read 2026-11-11).
  public static readonly string[] SportTypes =
  [
    "AlpineSki", "BackcountrySki", "Badminton", "Basketball", "Canoeing", "Cricket", "Crossfit", "Dance",
    "EBikeRide", "Elliptical", "EMountainBikeRide", "Golf", "GravelRide", "Handcycle",
    "HighIntensityIntervalTraining", "Hike", "IceSkate", "InlineSkate", "Kayaking", "Kitesurf",
    "MountainBikeRide", "NordicSki", "Padel", "PhysicalTherapy", "Pickleball", "Pilates", "Racquetball", "Ride",
    "RockClimbing", "RollerSki", "Rowing", "Run", "Sail", "Skateboard", "Snowboard", "Snowshoe", "Soccer",
    "Squash", "StairStepper", "StandUpPaddling", "Surfing", "Swim", "TableTennis", "Tennis", "TrailRun",
    "Velomobile", "VirtualRide", "VirtualRow", "VirtualRun", "Volleyball", "Walk", "WeightTraining", "Wheelchair",
    "Windsurf", "Workout", "Yoga",
  ];

  public override PluginManifest Manifest { get; } = new(
    ProofPlugins.Strava, "Strava", 1, ["ios", "android"],
    JsonSerializer.Serialize(new
    {
      type = "object",
      properties = new { sportTypes = new { type = "array", minItems = 1, items = new { type = "string", @enum = SportTypes } } },
      required = new[] { "sportTypes" },
      additionalProperties = false,
    }),
    "pro", false);

  protected override Dictionary<string, string[]> Validate(StravaSettings settings)
  {
    if (settings.SportTypes.Length == 0) return new() { ["sportTypes"] = ["Pick at least one sport type"] };
    var unknown = settings.SportTypes.Where(t => !SportTypes.Contains(t)).ToArray();
    return unknown.Length > 0 ? new() { ["sportTypes"] = [$"Unknown Strava sport type: {string.Join(", ", unknown)}"] } : [];
  }

  protected override async Task OnBind(BoundProof binding, StravaSettings settings, CancellationToken ct)
  {
    if (await connections.Get(LazyTax.Platform.Data.Owner.User(binding.UserId), ct) is null)
      throw new LazyTax.Platform.Problems.ValidationFailed("Invalid proof binding", "pluginKey", "Connect Strava before choosing it as proof");
  }

  protected override Task<ProofVerdict?> Evaluate(StravaSettings settings, StravaActivity payload, ProofEvent e, ProofWindow window, CancellationToken ct) =>
    Task.FromResult<ProofVerdict?>(!payload.Manual && settings.SportTypes.Contains(payload.SportType)
      ? new ProofVerdict(EvidenceVerdict.Passed)
      : null);
}
