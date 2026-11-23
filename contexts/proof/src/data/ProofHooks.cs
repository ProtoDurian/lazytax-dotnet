using Ctx.Habit.Contract.V1;
using Ctx.Proof.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Lifecycle;
using LazyTax.Platform.Outbox;
using Microsoft.Extensions.Logging;

namespace Ctx.Proof.Data;

/// <summary>Account deletion step 2, outside any transaction and best effort: revoke LazyTax at Strava.</summary>
public sealed class StravaBeforePurge(IStravaConnections connections, IStravaApi api, ILogger<StravaBeforePurge> logger) : IBeforePurge
{
  public async Task Run(string userId, CancellationToken ct)
  {
    try
    {
      if (await connections.Get(Owner.User(userId), ct) is { } c) await api.Revoke(c.Tokens.RefreshToken, ct);
    }
    catch (Exception e) when (e is not OperationCanceledException)
    {
      logger.LogWarning(e, "Strava revoke before purge failed for UserId={UserId}; continuing", userId);
    }
  }
}

/// <summary>A deleted habit releases its binding and NFC tags (LazyTax deleted <c>NfcTags</c> inside <c>HabitRepository.Delete</c>).</summary>
public sealed class HabitDeletedHandler(IBindingRepository bindings, INfcTagRepository tags) : IOutboxSubscriber<HabitDeleted>
{
  public async Task Handle(OutboxEvent meta, HabitDeleted e, CancellationToken ct)
  {
    await bindings.DeleteForHabits(e.UserId, e.HabitIds, ct);
    await tags.DeleteForHabits(e.UserId, e.HabitIds, ct);
  }
}
