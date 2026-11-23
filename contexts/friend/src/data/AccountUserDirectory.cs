using Ctx.Account.Contract.V1;
using Ctx.Friend.Domain;
using LazyTax.Platform.Data;

namespace Ctx.Friend.Data;

/// <summary>Usernames through account.v1; friend never reads the users table.</summary>
public sealed class AccountUserDirectory(IAccountV1 account) : IUserDirectory
{
  public async Task<FriendUser?> Get(string userId, CancellationToken ct)
  {
    var p = await account.GetProfile(Owner.User(userId), ct);
    return p is null ? null : new FriendUser(p.Id, p.Username);
  }

  public Task<IReadOnlyDictionary<string, string>> Usernames(IReadOnlyCollection<string> userIds, CancellationToken ct) =>
    userIds.Count == 0 ? Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()) : account.Usernames(userIds, ct);

  public async Task<IReadOnlyList<FriendUser>> Search(string excludeUserId, string query, int limit, CancellationToken ct) =>
    (await account.SearchUsernames(query, excludeUserId, limit, ct)).Select(u => new FriendUser(u.UserId, u.Username)).ToList();
}
