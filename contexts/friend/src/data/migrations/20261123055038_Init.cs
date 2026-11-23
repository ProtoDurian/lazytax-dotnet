using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ctx.Friend.data.migrations
{
  /// <inheritdoc />
  public partial class Init : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.EnsureSchema(
          name: "friend");

      migrationBuilder.CreateTable(
          name: "FriendInvites",
          schema: "friend",
          columns: table => new
          {
            Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_FriendInvites", x => x.Code);
          });

      migrationBuilder.CreateTable(
          name: "FriendRequests",
          schema: "friend",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            FromUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            ToUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_FriendRequests", x => x.Id);
          });

      migrationBuilder.CreateTable(
          name: "Friendships",
          schema: "friend",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            FriendUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_Friendships", x => x.Id);
          });

      migrationBuilder.CreateTable(
          name: "HabitFriendWatchers",
          schema: "friend",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            HabitId = table.Column<Guid>(type: "uuid", nullable: false),
            OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            FriendUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_HabitFriendWatchers", x => x.Id);
          });

      migrationBuilder.CreateIndex(
          name: "IX_FriendInvites_UserId",
          schema: "friend",
          table: "FriendInvites",
          column: "UserId",
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_FriendRequests_FromUserId_ToUserId",
          schema: "friend",
          table: "FriendRequests",
          columns: ["FromUserId", "ToUserId"],
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_FriendRequests_ToUserId",
          schema: "friend",
          table: "FriendRequests",
          column: "ToUserId");

      migrationBuilder.CreateIndex(
          name: "IX_Friendships_FriendUserId",
          schema: "friend",
          table: "Friendships",
          column: "FriendUserId");

      migrationBuilder.CreateIndex(
          name: "IX_Friendships_UserId_FriendUserId",
          schema: "friend",
          table: "Friendships",
          columns: ["UserId", "FriendUserId"],
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_HabitFriendWatchers_FriendUserId",
          schema: "friend",
          table: "HabitFriendWatchers",
          column: "FriendUserId");

      migrationBuilder.CreateIndex(
          name: "IX_HabitFriendWatchers_HabitId_FriendUserId",
          schema: "friend",
          table: "HabitFriendWatchers",
          columns: ["HabitId", "FriendUserId"],
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_HabitFriendWatchers_OwnerUserId",
          schema: "friend",
          table: "HabitFriendWatchers",
          column: "OwnerUserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(
          name: "FriendInvites",
          schema: "friend");

      migrationBuilder.DropTable(
          name: "FriendRequests",
          schema: "friend");

      migrationBuilder.DropTable(
          name: "Friendships",
          schema: "friend");

      migrationBuilder.DropTable(
          name: "HabitFriendWatchers",
          schema: "friend");
    }
  }
}
