using Ctx.Habit.Contract.V1;
using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;

namespace Ctx.Friend.Domain;

/// <summary>"Tell my friends if I miss": which friends hear when a habit's day ends missed.</summary>
public sealed class FriendStakeService(
  IFriendStakeRepository repo,
  FriendService friends,
  IUserDirectory users,
  IHabitV1 habits,
  IUnitOfWork uow)
{
  public const int MaxWatchers = 20;

  public async Task<IReadOnlyList<FriendUser>> GetWatchers(string userId, Guid habitId, CancellationToken ct)
  {
    await EnsureOwned(userId, habitId, ct);
    return await Resolve(await repo.GetWatcherIds(Owner.User(userId), habitId, ct), ct);
  }

  /// <summary>Replaces the habit's watchers. Each must be a friend of the user; an empty list turns it off.</summary>
  public async Task<IReadOnlyList<FriendUser>> SetWatchers(
    string userId, Guid habitId, IReadOnlyList<string> friendUserIds, CancellationToken ct)
  {
    await EnsureOwned(userId, habitId, ct);

    var ids = friendUserIds.Distinct().ToList();
    if (ids.Count > MaxWatchers)
      throw new ValidationFailed("Invalid request", "friendUserIds", $"At most {MaxWatchers} friends can be told");

    var notFriends = new List<string>();
    foreach (var id in ids)
      if (!await friends.AreFriends(userId, id, ct))
        notFriends.Add(id);
    if (notFriends.Count > 0)
      throw new ValidationFailed("Invalid request", "friendUserIds", $"Not your friends: {string.Join(", ", notFriends)}");

    await uow.Start(async () =>
    {
      // One writer per user: a concurrent replace of the same habit waits, then replaces again.
      await uow.LockUser(userId, ct);
      await repo.ReplaceWatchers(userId, habitId, ids, ct);
    }, ct);
    return await Resolve(await repo.GetWatcherIds(Owner.User(userId), habitId, ct), ct);
  }

  // The habit must be the user's own and not deleted; another user's habit reads as missing (404).
  private async Task EnsureOwned(string userId, Guid habitId, CancellationToken ct)
  {
    if (!await habits.Owns(userId, habitId, ct)) throw new NotFound("Habit", habitId.ToString());
  }

  private async Task<IReadOnlyList<FriendUser>> Resolve(IReadOnlyList<string> ids, CancellationToken ct)
  {
    var names = await users.Usernames(ids, ct);
    return ids
      .Where(names.ContainsKey)
      .Select(id => new FriendUser(id, names[id]))
      .OrderBy(u => u.Username, StringComparer.Ordinal)
      .ThenBy(u => u.UserId, StringComparer.Ordinal)
      .ToList();
  }
}
