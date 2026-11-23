using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ctx.Penalty.Data.Migrations
{
  /// <inheritdoc />
  public partial class Init : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.EnsureSchema(
          name: "penalty");

      migrationBuilder.CreateTable(
          name: "CharityBalances",
          schema: "penalty",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            CharityId = table.Column<Guid>(type: "uuid", nullable: false),
            Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
            AccruedCents = table.Column<long>(type: "bigint", nullable: false),
            UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_CharityBalances", x => x.Id);
          });

      migrationBuilder.CreateTable(
          name: "Penalties",
          schema: "penalty",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
            HabitExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
            HabitId = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            CharityId = table.Column<Guid>(type: "uuid", nullable: false),
            AmountCents = table.Column<long>(type: "bigint", nullable: false),
            Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
            Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
            PaymentIntentId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
            Attempts = table.Column<int>(type: "integer", nullable: false),
            LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
            DisbursementId = table.Column<Guid>(type: "uuid", nullable: true),
            CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_Penalties", x => x.Id);
            table.CheckConstraint("CK_Penalties_AmountCents", "\"AmountCents\" > 0");
            table.CheckConstraint("CK_Penalties_Status", "\"Status\" IN ('pending', 'processing', 'charged', 'failed', 'skipped')");
          });

      migrationBuilder.CreateIndex(
          name: "IX_CharityBalances_CharityId_Currency",
          schema: "penalty",
          table: "CharityBalances",
          columns: ["CharityId", "Currency"],
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_Penalties_CharityId_Currency",
          schema: "penalty",
          table: "Penalties",
          columns: ["CharityId", "Currency"],
          filter: "\"Status\" = 'charged' AND \"DisbursementId\" IS NULL");

      migrationBuilder.CreateIndex(
          name: "IX_Penalties_DisbursementId",
          schema: "penalty",
          table: "Penalties",
          column: "DisbursementId",
          filter: "\"DisbursementId\" IS NOT NULL");

      migrationBuilder.CreateIndex(
          name: "IX_Penalties_HabitExecutionId",
          schema: "penalty",
          table: "Penalties",
          column: "HabitExecutionId",
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_Penalties_Status_CreatedAt",
          schema: "penalty",
          table: "Penalties",
          columns: ["Status", "CreatedAt"]);

      migrationBuilder.CreateIndex(
          name: "IX_Penalties_UserId_CreatedAt",
          schema: "penalty",
          table: "Penalties",
          columns: ["UserId", "CreatedAt"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(
          name: "CharityBalances",
          schema: "penalty");

      migrationBuilder.DropTable(
          name: "Penalties",
          schema: "penalty");
    }
  }
}
