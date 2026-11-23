using Microsoft.EntityFrameworkCore;

namespace Ctx.Friend.Data;

public sealed class FriendDb(DbContextOptions<FriendDb> options) : DbContext(options)
{
  public DbSet<FriendRequestRow> FriendRequests => this.Set<FriendRequestRow>();
  public DbSet<FriendshipRow> Friendships => this.Set<FriendshipRow>();
  public DbSet<FriendInviteRow> FriendInvites => this.Set<FriendInviteRow>();
  public DbSet<WatcherRow> HabitFriendWatchers => this.Set<WatcherRow>();

  protected override void OnModelCreating(ModelBuilder b)
  {
    b.HasDefaultSchema("friend");

    // One pending request per ordered pair, one friendship row per direction.
    b.Entity<FriendRequestRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.HasIndex(x => new { x.FromUserId, x.ToUserId }).IsUnique();
      e.HasIndex(x => x.ToUserId);
    });
    b.Entity<FriendshipRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.HasIndex(x => new { x.UserId, x.FriendUserId }).IsUnique();
      e.HasIndex(x => x.FriendUserId);
    });
    b.Entity<FriendInviteRow>(e =>
    {
      e.HasKey(x => x.Code);
      e.HasIndex(x => x.UserId).IsUnique();
    });
    // One row per (habit, friend). OwnerUserId is a copy: habit rows live in another context.
    b.Entity<WatcherRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.HasIndex(x => new { x.HabitId, x.FriendUserId }).IsUnique();
      e.HasIndex(x => x.FriendUserId);
      e.HasIndex(x => x.OwnerUserId);
    });
  }
}
