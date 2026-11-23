using System.Globalization;
using Ctx.Check.Contract.V1;
using Ctx.Habit.Contract.V1;
using Ctx.Proof.Contract.V1;
using Ctx.Proof.Domain;

namespace Ctx.Proof.Api.V1;

// Settings is a JSON object as text; its shape is the plugin's SettingsSchema.
public sealed record BindProofReq(string PluginKey, string Settings);

// A habit with no binding reports the manual proof.
public sealed record ProofBindingRes(Guid HabitId, string PluginKey, int PluginVersion, string Settings);

public sealed record PluginManifestRes(string Key, string Name, int Version, string[] Platforms, string SettingsSchema, string? MinTier, bool AlwaysBound);

public sealed record LinkNfcTagReq(Guid HabitId);

public sealed record NfcTagRes(string Id, string UserId, Guid HabitId, string ClaimedAt);

public sealed record HabitVersionRes(
  Guid Id, Guid HabitId, ushort Version, string Task, string[] DaysOfWeek, string NotificationTime, string Stake, string Ratio,
  Guid? CharityId, string Timezone, string DeadlineTime);

public sealed record HabitExecutionRes(Guid Id, Guid HabitVersionId, string Date, string Status, string? CompletedAt, string? Notes, bool PaymentProcessed);

public sealed record NfcTagResolutionRes(NfcTagRes Tag, HabitVersionRes HabitVersion, string Today, HabitExecutionRes? TodayExecution);

public sealed record TapRes(Guid HabitId, Guid? ExecutionId);

public sealed record StravaConnectUrlRes(string Url, string AppUrl);

public sealed record StravaConnectionRes(bool Connected, DateTime? ConnectedAtUtc);

public static class Mapper
{
  private const string DateFormat = "dd-MM-yyyy";
  private const string TimeFormat = "HH:mm:ss";

  public static ProofBindingRes ToRes(this BoundProof b) => new(b.HabitId, b.PluginKey, b.PluginVersion, b.Settings);

  public static PluginManifestRes ToRes(this PluginManifest m) => new(m.Key, m.Name, m.Version, m.Platforms, m.SettingsSchema, m.MinTier, m.AlwaysBound);

  public static NfcTagRes ToRes(this NfcTag t) => new(t.Id, t.UserId, t.HabitId, t.ClaimedAtUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));

  public static HabitVersionRes ToRes(this HabitVersionView v) => new(
    v.Id, v.HabitId, v.Version, v.Task, v.DaysOfWeek, v.NotificationTime.ToString(TimeFormat, CultureInfo.InvariantCulture),
    (v.StakeCents / 100m).ToString("F2", CultureInfo.InvariantCulture), (v.RatioBasisPoints / 100m).ToString("F1", CultureInfo.InvariantCulture),
    v.CharityId, v.Timezone, v.DeadlineTime.ToString(TimeFormat, CultureInfo.InvariantCulture));

  public static HabitExecutionRes ToRes(this HabitExecution e) => new(
    e.Id, e.HabitVersionId, e.Date.ToString(DateFormat, CultureInfo.InvariantCulture), e.Status switch
    {
      CheckStatus.Done => "succeeded",
      CheckStatus.Skipped => "skip",
      CheckStatus.Frozen => "frozen",
      CheckStatus.Vacation => "vacation",
      _ => "failed",
    },
    e.CompletedAtUtc?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture), e.Notes, false);

  public static NfcTagResolutionRes ToRes(this NfcResolution r) => new(
    r.Tag.ToRes(), r.HabitVersion.ToRes(), r.Today.ToString(DateFormat, CultureInfo.InvariantCulture), r.TodayExecution?.ToRes());
}
