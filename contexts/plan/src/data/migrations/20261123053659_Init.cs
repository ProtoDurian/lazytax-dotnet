using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ctx.Plan.data.migrations
{
  /// <inheritdoc />
  public partial class Init : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.EnsureSchema(
          name: "plan");

      migrationBuilder.CreateTable(
          name: "UserSubscriptions",
          schema: "plan",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            Tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
            Status = table.Column<int>(type: "integer", nullable: false),
            PeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            PeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            CancelAtPeriodEnd = table.Column<bool>(type: "boolean", nullable: false),
            NextTier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
            CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_UserSubscriptions", x => x.Id);
          });

      migrationBuilder.CreateIndex(
          name: "IX_UserSubscriptions_UserId",
          schema: "plan",
          table: "UserSubscriptions",
          column: "UserId",
          unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(
          name: "UserSubscriptions",
          schema: "plan");
    }
  }
}
