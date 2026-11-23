using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ctx.Notification.data.migrations
{
  /// <inheritdoc />
  public partial class Init : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.EnsureSchema(
          name: "notification");

      migrationBuilder.CreateTable(
          name: "InboxNotifications",
          schema: "notification",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
            Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
            Body = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
            ActorUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
            Data = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
            DedupeKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
            CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            ReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_InboxNotifications", x => x.Id);
          });

      migrationBuilder.CreateIndex(
          name: "IX_InboxNotifications_ActorUserId",
          schema: "notification",
          table: "InboxNotifications",
          column: "ActorUserId");

      migrationBuilder.CreateIndex(
          name: "IX_InboxNotifications_UserId_CreatedAtUtc",
          schema: "notification",
          table: "InboxNotifications",
          columns: new[] { "UserId", "CreatedAtUtc" });

      migrationBuilder.CreateIndex(
          name: "IX_InboxNotifications_UserId_DedupeKey",
          schema: "notification",
          table: "InboxNotifications",
          columns: new[] { "UserId", "DedupeKey" },
          unique: true,
          filter: "\"DedupeKey\" IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(
          name: "InboxNotifications",
          schema: "notification");
    }
  }
}
