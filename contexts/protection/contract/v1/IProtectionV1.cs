using LazyTax.Platform.Data;

namespace Ctx.Protection.Contract.V1;

/// <summary>protection.v1: the user's freeze-day pool (LazyTax Domain/Protection).</summary>
public interface IProtectionV1
{
  /// <summary>
  /// Cuts the user's banked freezes to the cap for this max streak (plan.v1 FreezeCap) and returns the balance and
  /// cap. Cut freezes are gone; a later higher cap does not restore them. check passes the user's max streak across
  /// habits; protection remembers it for <see cref="Balance"/>.
  /// </summary>
  Task<FreezeBalance> ReconcileCap(string userId, int maxStreak, CancellationToken ct = default);

  /// <summary>
  /// Spends one freeze day for this user-local date. True when the date is frozen (already consumed for that date,
  /// or one was just spent); false when the balance is 0. Idempotent per (user, date). Joins the caller's unit of
  /// work and takes the user lock.
  /// </summary>
  Task<bool> TryConsumeFreeze(string userId, DateOnly date, CancellationToken ct = default);

  /// <summary>The balance of <c>Owner.User(id)</c>, cut to the cap first. <see cref="Owner.Anyone"/> is refused.</summary>
  Task<FreezeBalance> Balance(Owner owner, CancellationToken ct = default);
}

/// <summary>Banked freeze days (never above Cap) and the user's current cap.</summary>
public sealed record FreezeBalance(string UserId, int Balance, int Cap);
