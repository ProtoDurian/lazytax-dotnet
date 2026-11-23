using LazyTax.Platform.Problems;

namespace Ctx.Plan.Contract.V1;

/// <summary>plan.v1: the user's effective tier and its entitlements (LazyTax Subscription + Entitlement).</summary>
public interface IPlanV1
{
  /// <summary>The effective tier now: an active (or in-grace) paid tier, else <see cref="Tiers.Free"/>.</summary>
  Task<string> EffectiveTier(string userId, CancellationToken ct = default);

  /// <summary>
  /// A counted limit of the user's plan. The caller counts and writes against it in ONE unit of work,
  /// after <c>uow.LockUser(userId)</c>, and throws <see cref="TierInsufficient"/> when the count reaches Limit.
  /// </summary>
  Task<Quota> Quota(string userId, QuotaKind kind, CancellationToken ct = default);

  /// <summary>The user's freeze cap: the tier's freeze base plus the streak bonus (LazyTax GetFreezeCapForUser).</summary>
  Task<int> FreezeCap(string userId, int maxStreak, CancellationToken ct = default);

  /// <summary>Throws <see cref="TierInsufficient"/> unless the user's tier costs at least <paramref name="minTier"/>.</summary>
  Task EnsureTier(string userId, string minTier, string feature, CancellationToken ct = default);
}

public enum QuotaKind
{
  Habits,
  Skips,
  VacationWindows,
}

/// <summary>A counted limit: <paramref name="Key"/> is LazyTax's entitlement key, e.g. <c>ent.vacation.windows.yearly</c>.</summary>
public sealed record Quota(string Tier, string Key, int Limit);

public static class Tiers
{
  public const string Free = "free";
  public const string Pro = "pro";
  public const string Ultimate = "ultimate";
}

public static class EntitlementKeys
{
  public const string HabitsMax = "ent.habits.max";
  public const string SkipsMonthly = "ent.skips.monthly";
  public const string VacationWindowsYearly = "ent.vacation.windows.yearly";
  public const string FreezeBase = "ent.freeze.base";

  public static string For(QuotaKind kind) => kind switch
  {
    QuotaKind.Habits => HabitsMax,
    QuotaKind.Skips => SkipsMonthly,
    QuotaKind.VacationWindows => VacationWindowsYearly,
    _ => throw new ArgumentOutOfRangeException(nameof(kind)),
  };
}

/// <summary>400 (as LazyTax): the user's tier does not allow this.</summary>
public sealed class TierInsufficient(string tier, string limitKey, int limitValue, string? detail = null)
  : Problem(400, "tier_insufficient", "Tier Insufficient",
    detail ?? (limitValue <= 0
      ? $"Your {tier} plan does not allow this action."
      : $"Your {tier} plan has reached its limit of {limitValue} for '{limitKey}'."),
    new { tier, limitKey, limitValue });
