using Ctx.Account.Contract.V1;
using Ctx.Account.Data;
using Ctx.Account.Domain;
using LazyTax.Platform.Data;
using LazyTax.Platform.Problems;
using Microsoft.EntityFrameworkCore;

namespace Ctx.Account;

/// <summary>account.v1 for other contexts.</summary>
public sealed class AccountV1(AccountDb db) : IAccountV1
{
  public async Task<AccountProfile?> GetProfile(Owner owner, CancellationToken ct = default)
  {
    if (owner.IsAnyone) throw new ArgumentException("GetProfile needs a user owner", nameof(owner));
    var u = await db.Users.AsNoTracking().OwnedBy(owner, x => x.Id).FirstOrDefaultAsync(ct);
    return u is null ? null : new AccountProfile(u.Id, u.Username, u.Email, u.EmailVerified, u.Active);
  }

  public Task<bool> Exists(string userId, CancellationToken ct = default) => db.Users.AnyAsync(x => x.Id == userId, ct);

  public Task<string?> GetTimezone(string userId, CancellationToken ct = default) =>
    db.Configurations.AsNoTracking().Where(x => x.UserId == userId).Select(x => (string?)x.Timezone).FirstOrDefaultAsync(ct);

  public async Task<TimeOnly> EndOfDay(string userId, CancellationToken ct = default) =>
    await db.Configurations.AsNoTracking().Where(x => x.UserId == userId).Select(x => (TimeOnly?)x.EndOfDay).FirstOrDefaultAsync(ct)
    ?? new TimeOnly(23, 59);

  public Task<Guid?> DefaultCharity(string userId, CancellationToken ct = default) =>
    db.Configurations.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.DefaultCharityId).FirstOrDefaultAsync(ct);

  public async Task<UserToday> Today(string userId, DateTime? nowUtc = null, CancellationToken ct = default)
  {
    var tz = await this.GetTimezone(userId, ct) ?? throw new NotFound("Configuration", userId);
    return new UserToday(tz, Timezones.Today(tz, nowUtc ?? DateTime.UtcNow));
  }

  public async Task<IReadOnlyList<UsernameRef>> SearchUsernames(string prefix, string excludeUserId, int limit, CancellationToken ct = default)
  {
    var take = Math.Clamp(limit, 1, 100);
    var pattern = prefix.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
    var rows = await db.Users.AsNoTracking()
      .Where(x => x.Id != excludeUserId && EF.Functions.ILike(x.Username, pattern, "\\"))
      .OrderBy(x => x.Username.ToLower()).ThenBy(x => x.Username).ThenBy(x => x.Id)
      .Take(take)
      .Select(x => new UsernameRef(x.Id, x.Username))
      .ToListAsync(ct);
    return rows;
  }

  public async Task<IReadOnlyDictionary<string, string>> Usernames(IReadOnlyCollection<string> userIds, CancellationToken ct = default)
  {
    if (userIds.Count == 0) return new Dictionary<string, string>();
    var ids = userIds.Distinct().ToList();
    return await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Username, ct);
  }
}
