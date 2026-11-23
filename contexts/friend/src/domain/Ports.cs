using LazyTax.Platform.Data;

namespace Ctx.Friend.Domain;

/// <summary>Usernames live in account; friend reaches them through account.v1.</summary>
public interface IUserDirectory
{
  Task<FriendUser?> Get(string userId, CancellationToken ct);

  Task<IReadOnlyDictionary<string, string>> Usernames(IReadOnlyCollection<string> userIds, CancellationToken ct);

  /// <summary>Usernames starting with <paramref name="query"/> (case-insensitive), excluding <paramref name="excludeUserId"/>.</summary>
  Task<IReadOnlyList<FriendUser>> Search(string excludeUserId, string query, int limit, CancellationToken ct);
}

/// <summary>Every read takes an <see cref="Owner"/>: the rows read belong to that user (as either side of a request).</summary>
public interface IFriendRepository
{
  Task<FriendRelation> Relation(Owner owner, string otherUserId, CancellationToken ct);

  /// <summary>How each of <paramref name="ids"/> relates to the owner.</summary>
  Task<IReadOnlyDictionary<string, FriendRelation>> Relations(Owner owner, IReadOnlyCollection<string> ids, CancellationToken ct);

  /// <summary>Idempotent: the same ordered pair twice leaves one request.</summary>
  Task CreateRequest(string fromUserId, string toUserId, CancellationToken ct);

  Task<IReadOnlyList<RequestRow>> ListRequests(Owner owner, CancellationToken ct);

  /// <summary>Deletes the request either way (owner to other or other to owner). False when there was none.</summary>
  Task<bool> DeleteRequest(Owner owner, string otherUserId, CancellationToken ct);

  /// <summary>Creates both directions and deletes any request between them. Idempotent.</summary>
  Task<DateTime> CreateFriendship(string userId, string friendUserId, CancellationToken ct);

  Task<IReadOnlyList<FriendshipRow>> ListFriends(Owner owner, CancellationToken ct);

  /// <summary>Deletes both directions and the watchers each had on the other's habits. False when they were not friends.</summary>
  Task<bool> DeleteFriendship(Owner owner, string friendUserId, CancellationToken ct);

  Task<FriendInvitePrincipal> GetOrCreateInvite(string userId, string newCode, CancellationToken ct);

  Task<FriendInvitePrincipal?> GetInvite(string code, CancellationToken ct);
}

public sealed record RequestRow(string FromUserId, string ToUserId, DateTime CreatedAtUtc);

public sealed record FriendshipRow(string FriendUserId, DateTime SinceUtc);

public interface IFriendStakeRepository
{
  /// <summary>Watchers of the habit that are still friends with its owner.</summary>
  Task<IReadOnlyList<string>> GetWatcherIds(Owner owner, Guid habitId, CancellationToken ct);

  Task ReplaceWatchers(string ownerUserId, Guid habitId, IReadOnlyCollection<string> friendUserIds, CancellationToken ct);

  /// <summary>Drops every watcher of the owner's habits (a habit was deleted).</summary>
  Task DeleteForHabits(string ownerUserId, IReadOnlyCollection<Guid> habitIds, CancellationToken ct);
}
