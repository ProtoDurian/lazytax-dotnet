using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ctx.Vacation.data.migrations
{
  /// <inheritdoc />
  public partial class Init : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.EnsureSchema(
          name: "vacation");

      migrationBuilder.CreateTable(
          name: "VacationPeriods",
          schema: "vacation",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            StartDate = table.Column<DateOnly>(type: "date", nullable: false),
            EndDate = table.Column<DateOnly>(type: "date", nullable: false),
            Timezone = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
            CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_VacationPeriods", x => x.Id);
          });

      migrationBuilder.CreateIndex(
          name: "IX_VacationPeriods_UserId_StartDate",
          schema: "vacation",
          table: "VacationPeriods",
          columns: ["UserId", "StartDate"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(
          name: "VacationPeriods",
          schema: "vacation");
    }
  }
}
