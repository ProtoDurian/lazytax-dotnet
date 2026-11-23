using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ctx.Protection.data.migrations
{
  /// <inheritdoc />
  public partial class Init : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.EnsureSchema(
          name: "protection");

      migrationBuilder.CreateTable(
          name: "FreezeAwards",
          schema: "protection",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            HabitId = table.Column<Guid>(type: "uuid", nullable: false),
            WeekStart = table.Column<DateOnly>(type: "date", nullable: false),
            AwardedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_FreezeAwards", x => x.Id);
          });

      migrationBuilder.CreateTable(
          name: "FreezeConsumptions",
          schema: "protection",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            Date = table.Column<DateOnly>(type: "date", nullable: false),
            ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_FreezeConsumptions", x => x.Id);
          });

      migrationBuilder.CreateTable(
          name: "SettledDays",
          schema: "protection",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            HabitId = table.Column<Guid>(type: "uuid", nullable: false),
            Date = table.Column<DateOnly>(type: "date", nullable: false),
            Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
            SettledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_SettledDays", x => x.Id);
          });

      migrationBuilder.CreateTable(
          name: "UserProtections",
          schema: "protection",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            FreezeCurrent = table.Column<int>(type: "integer", nullable: false),
            MaxStreak = table.Column<int>(type: "integer", nullable: false),
            UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_UserProtections", x => x.Id);
          });

      migrationBuilder.CreateIndex(
          name: "IX_FreezeAwards_HabitId_WeekStart",
          schema: "protection",
          table: "FreezeAwards",
          columns: ["HabitId", "WeekStart"],
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_FreezeAwards_UserId",
          schema: "protection",
          table: "FreezeAwards",
          column: "UserId");

      migrationBuilder.CreateIndex(
          name: "IX_FreezeConsumptions_UserId_Date",
          schema: "protection",
          table: "FreezeConsumptions",
          columns: ["UserId", "Date"],
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_SettledDays_HabitId_Date",
          schema: "protection",
          table: "SettledDays",
          columns: ["HabitId", "Date"],
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_SettledDays_UserId_Date",
          schema: "protection",
          table: "SettledDays",
          columns: ["UserId", "Date"]);

      migrationBuilder.CreateIndex(
          name: "IX_UserProtections_UserId",
          schema: "protection",
          table: "UserProtections",
          column: "UserId",
          unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(
          name: "FreezeAwards",
          schema: "protection");

      migrationBuilder.DropTable(
          name: "FreezeConsumptions",
          schema: "protection");

      migrationBuilder.DropTable(
          name: "SettledDays",
          schema: "protection");

      migrationBuilder.DropTable(
          name: "UserProtections",
          schema: "protection");
    }
  }
}
