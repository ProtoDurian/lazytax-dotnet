namespace Ctx.Plan.Domain;

/// <summary>One subscription row per user. Reads are keyed by the user they belong to.</summary>
public interface ISubscriptionRepository
{
  Task<SubscriptionPrincipal?> GetByUser(string userId, CancellationToken ct);

  /// <summary>Insert or replace the user's one row.</summary>
  Task<SubscriptionPrincipal> Upsert(SubscriptionRecord record, CancellationToken ct);
}
