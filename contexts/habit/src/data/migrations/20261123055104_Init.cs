using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ctx.Habit.data.migrations
{
  /// <inheritdoc />
  public partial class Init : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.EnsureSchema(
          name: "habit");

      migrationBuilder.CreateTable(
          name: "Routines",
          schema: "habit",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
            CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_Routines", x => x.Id);
          });

      migrationBuilder.CreateTable(
          name: "Habits",
          schema: "habit",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            Version = table.Column<int>(type: "integer", nullable: false),
            Enabled = table.Column<bool>(type: "boolean", nullable: false),
            SortOrder = table.Column<int>(type: "integer", nullable: false),
            RoutineId = table.Column<Guid>(type: "uuid", nullable: true),
            DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_Habits", x => x.Id);
            table.ForeignKey(
                      name: "FK_Habits_Routines_RoutineId",
                      column: x => x.RoutineId,
                      principalSchema: "habit",
                      principalTable: "Routines",
                      principalColumn: "Id",
                      onDelete: ReferentialAction.Restrict);
          });

      migrationBuilder.CreateTable(
          name: "HabitVersions",
          schema: "habit",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            HabitId = table.Column<Guid>(type: "uuid", nullable: false),
            CharityId = table.Column<Guid>(type: "uuid", nullable: true),
            Version = table.Column<int>(type: "integer", nullable: false),
            Task = table.Column<string>(type: "text", nullable: false),
            DaysOfWeek = table.Column<string[]>(type: "text[]", nullable: false),
            NotificationTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
            DeadlineTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
            StakeCents = table.Column<int>(type: "integer", nullable: false),
            StakeCurrency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
            RatioBasisPoints = table.Column<int>(type: "integer", nullable: false),
            Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_HabitVersions", x => x.Id);
            table.ForeignKey(
                      name: "FK_HabitVersions_Habits_HabitId",
                      column: x => x.HabitId,
                      principalSchema: "habit",
                      principalTable: "Habits",
                      principalColumn: "Id",
                      onDelete: ReferentialAction.Cascade);
          });

      migrationBuilder.CreateIndex(
          name: "IX_Habits_RoutineId",
          schema: "habit",
          table: "Habits",
          column: "RoutineId");

      migrationBuilder.CreateIndex(
          name: "IX_Habits_UserId_DeletedAt",
          schema: "habit",
          table: "Habits",
          columns: new[] { "UserId", "DeletedAt" });

      migrationBuilder.CreateIndex(
          name: "IX_HabitVersions_HabitId_Version",
          schema: "habit",
          table: "HabitVersions",
          columns: new[] { "HabitId", "Version" },
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_Routines_UserId",
          schema: "habit",
          table: "Routines",
          column: "UserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(
          name: "HabitVersions",
          schema: "habit");

      migrationBuilder.DropTable(
          name: "Habits",
          schema: "habit");

      migrationBuilder.DropTable(
          name: "Routines",
          schema: "habit");
    }
  }
}
