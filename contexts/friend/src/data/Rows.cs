using System.ComponentModel.DataAnnotations;

namespace Ctx.Friend.Data;

/// <summary>A pending request from FromUserId to ToUserId. Accepting or declining deletes it.</summary>
public sealed class FriendRequestRow
{
  public Guid Id { get; set; }

  [MaxLength(128)] public required string FromUserId { get; set; }

  [MaxLength(128)] public required string ToUserId { get; set; }

  public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>One row per direction: a friendship between A and B is (A, B) and (B, A).</summary>
public sealed class FriendshipRow
{
  public Guid Id { get; set; }

  [MaxLength(128)] public required string UserId { get; set; }

  [MaxLength(128)] public required string FriendUserId { get; set; }

  public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A user's permanent invite link (one per user, unique on UserId); it never expires.</summary>
public sealed class FriendInviteRow
{
  [MaxLength(32)] public required string Code { get; set; }

  [MaxLength(128)] public required string UserId { get; set; }

  public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A friend told when the habit's day ends missed.</summary>
public sealed class WatcherRow
{
  public Guid Id { get; set; }

  public Guid HabitId { get; set; }

  [MaxLength(128)] public required string OwnerUserId { get; set; }

  [MaxLength(128)] public required string FriendUserId { get; set; }

  public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
