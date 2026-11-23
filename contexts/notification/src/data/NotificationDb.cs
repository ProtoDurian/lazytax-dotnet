using Microsoft.EntityFrameworkCore;

namespace Ctx.Notification.Data;

public sealed class NotificationDb(DbContextOptions<NotificationDb> options) : DbContext(options)
{
  public DbSet<InboxNotificationRow> InboxNotifications => this.Set<InboxNotificationRow>();

  protected override void OnModelCreating(ModelBuilder b)
  {
    b.HasDefaultSchema("notification");
    b.Entity<InboxNotificationRow>(e =>
    {
      e.HasKey(x => x.Id);
      e.Property(x => x.UserId).HasMaxLength(128);
      e.Property(x => x.Kind).HasMaxLength(32);
      e.Property(x => x.Title).HasMaxLength(256);
      e.Property(x => x.Body).HasMaxLength(1024);
      e.Property(x => x.ActorUserId).HasMaxLength(128);
      e.Property(x => x.Data).HasMaxLength(2048);
      e.Property(x => x.DedupeKey).HasMaxLength(128);
      e.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
      e.HasIndex(x => x.ActorUserId);
      // Posting the same key to the same user twice records it once (outbox retries).
      e.HasIndex(x => new { x.UserId, x.DedupeKey }).IsUnique().HasFilter("\"DedupeKey\" IS NOT NULL");
    });
  }
}

/// <summary>One in-app notification. Deleted with its recipient's or its actor's account.</summary>
public sealed class InboxNotificationRow
{
  public Guid Id { get; set; }
  public required string UserId { get; set; }
  public required string Kind { get; set; }
  public required string Title { get; set; }
  public required string Body { get; set; }

  /// <summary>The user who caused it (a copy of their id, no foreign key); null for system notifications.</summary>
  public string? ActorUserId { get; set; }

  public string? Data { get; set; }
  public string? DedupeKey { get; set; }
  public DateTime CreatedAtUtc { get; set; }
  public DateTime? ReadAtUtc { get; set; }
}
