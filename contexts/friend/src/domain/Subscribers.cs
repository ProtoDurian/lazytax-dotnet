using System.Globalization;
using System.Text.Json;
using Ctx.Friend.Contract.V1;
using Ctx.Habit.Contract.V1;
using Ctx.Notification.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Outbox;

namespace Ctx.Friend.Domain;

/// <summary>Posts "wants to be friends" to the person asked, after the request committed.</summary>
public sealed class FriendRequestSentHandler(INotificationV1 inbox, IUserDirectory users) : IOutboxSubscriber<FriendRequestSent>
{
  public async Task Handle(OutboxEvent meta, FriendRequestSent e, CancellationToken ct)
  {
    var sender = await users.Get(e.FromUserId, ct);
    if (sender is null) return; // the sender is gone: nothing left to tell
    await inbox.Post(e.ToUserId, InboxKinds.FriendRequest, "Friend request", $"{sender.Username} wants to be friends",
      actorUserId: e.FromUserId, data: JsonSerializer.Serialize(new { fromUserId = e.FromUserId }),
      dedupeKey: $"{InboxKinds.FriendRequest}:{e.FromUserId}:{meta.Id}", ct: ct);
  }
}

/// <summary>Tells the requester their request was accepted.</summary>
public sealed class FriendAcceptedHandler(INotificationV1 inbox, IUserDirectory users) : IOutboxSubscriber<FriendAccepted>
{
  public async Task Handle(OutboxEvent meta, FriendAccepted e, CancellationToken ct)
  {
    var accepter = await users.Get(e.AccepterUserId, ct);
    if (accepter is null) return;
    await inbox.Post(e.RequesterUserId, InboxKinds.FriendAccepted, "New friend", $"{accepter.Username} is now your friend",
      actorUserId: e.AccepterUserId, data: JsonSerializer.Serialize(new { friendUserId = e.AccepterUserId }),
      // Two concurrent accepts of the same request end up with the same friendship row (and so the same
      // SinceTicks): only one notification. A later re-friending gets new ticks.
      dedupeKey: $"{InboxKinds.FriendAccepted}:{e.AccepterUserId}:{e.SinceTicks}", ct: ct);
  }
}

/// <summary>The subscriber's own record of <c>check.settled</c>: only the fields friend reads.</summary>
public sealed record CheckSettled(string UserId, Guid HabitId, Guid ExecutionId, DateOnly Date, string Status);

/// <summary>
/// "Tell my friends if I miss": a missed check posts one friend_missed notification to each friend watching the
/// habit. Friends only learn that it was missed, never proof details. The delivery is one unit of work, so a failed
/// post rolls back the others and the outbox retries the whole event; the dedupe key keeps a retry from doubling.
/// </summary>
public sealed class CheckSettledHandler(
  IFriendStakeRepository watchers, IHabitV1 habits, IUserDirectory users, INotificationV1 inbox)
  : IOutboxSubscriber<CheckSettled>
{
  private const int MaxTaskLength = 200;
  private const int MaxTitleLength = 256;

  public async Task Handle(OutboxEvent meta, CheckSettled e, CancellationToken ct)
  {
    if (!string.Equals(e.Status, "missed", StringComparison.OrdinalIgnoreCase)) return;

    var owner = Owner.User(e.UserId);
    var ids = await watchers.GetWatcherIds(owner, e.HabitId, ct);
    if (ids.Count == 0) return;

    var habit = await habits.GetCurrent(owner, e.HabitId, ct);
    var user = await users.Get(e.UserId, ct);
    // The habit or the owner is gone: nothing left to tell.
    if (habit is null || user is null) return;

    var task = habit.Task.Length > MaxTaskLength ? habit.Task[..(MaxTaskLength - 1)] + "…" : habit.Task;
    var title = $"{user.Username} missed a habit";
    var body = $"{user.Username} missed \"{task}\" on {e.Date.ToString("ddd d MMM", CultureInfo.InvariantCulture)}";
    var data = JsonSerializer.Serialize(new
    {
      habitId = e.HabitId,
      userId = e.UserId,
      localDate = e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    });
    foreach (var id in ids)
      await inbox.Post(id, InboxKinds.FriendMissed, title.Length > MaxTitleLength ? title[..MaxTitleLength] : title,
        body, actorUserId: e.UserId, data: data, dedupeKey: $"friend_missed:{e.ExecutionId}", ct: ct);
  }
}

/// <summary>A deleted habit tells nobody anything: drop its watchers.</summary>
public sealed class HabitDeletedHandler(IFriendStakeRepository watchers) : IOutboxSubscriber<HabitDeleted>
{
  public Task Handle(OutboxEvent meta, HabitDeleted e, CancellationToken ct) =>
    watchers.DeleteForHabits(e.UserId, e.HabitIds, ct);
}
