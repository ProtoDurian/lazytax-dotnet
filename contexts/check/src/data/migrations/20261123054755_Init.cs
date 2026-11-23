using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ctx.Check.data.migrations
{
  /// <inheritdoc />
  public partial class Init : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.EnsureSchema(
          name: "check");

      migrationBuilder.CreateTable(
          name: "HabitChecks",
          schema: "check",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            HabitId = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
            Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            OpensAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            DeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            Status = table.Column<byte>(type: "smallint", nullable: false),
            SettledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            SettledBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
            ExecutionId = table.Column<Guid>(type: "uuid", nullable: true)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_HabitChecks", x => x.Id);
          });

      migrationBuilder.CreateTable(
          name: "HabitEvidence",
          schema: "check",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            HabitId = table.Column<Guid>(type: "uuid", nullable: false),
            CheckId = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            ProofKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
            Passed = table.Column<bool>(type: "boolean", nullable: false),
            OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            Data = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_HabitEvidence", x => x.Id);
          });

      migrationBuilder.CreateTable(
          name: "HabitExecutions",
          schema: "check",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            HabitId = table.Column<Guid>(type: "uuid", nullable: false),
            HabitVersionId = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            Date = table.Column<DateOnly>(type: "date", nullable: false),
            Status = table.Column<byte>(type: "smallint", nullable: false),
            CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            Notes = table.Column<string>(type: "text", nullable: true),
            PaymentProcessed = table.Column<bool>(type: "boolean", nullable: false),
            StakeCents = table.Column<long>(type: "bigint", nullable: false),
            RatioBps = table.Column<int>(type: "integer", nullable: false),
            Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
            CharityId = table.Column<Guid>(type: "uuid", nullable: true),
            HabitDeleted = table.Column<bool>(type: "boolean", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_HabitExecutions", x => x.Id);
          });

      migrationBuilder.CreateIndex(
          name: "IX_HabitChecks_HabitId_LocalDate",
          schema: "check",
          table: "HabitChecks",
          columns: new[] { "HabitId", "LocalDate" },
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_HabitChecks_Status_DeadlineUtc",
          schema: "check",
          table: "HabitChecks",
          columns: new[] { "Status", "DeadlineUtc" });

      migrationBuilder.CreateIndex(
          name: "IX_HabitChecks_UserId",
          schema: "check",
          table: "HabitChecks",
          column: "UserId");

      migrationBuilder.CreateIndex(
          name: "IX_HabitEvidence_HabitId_ProofKey_ExternalId",
          schema: "check",
          table: "HabitEvidence",
          columns: new[] { "HabitId", "ProofKey", "ExternalId" },
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_HabitEvidence_UserId",
          schema: "check",
          table: "HabitEvidence",
          column: "UserId");

      migrationBuilder.CreateIndex(
          name: "IX_HabitExecutions_HabitId_Date",
          schema: "check",
          table: "HabitExecutions",
          columns: new[] { "HabitId", "Date" },
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_HabitExecutions_UserId_Date",
          schema: "check",
          table: "HabitExecutions",
          columns: new[] { "UserId", "Date" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(
          name: "HabitChecks",
          schema: "check");

      migrationBuilder.DropTable(
          name: "HabitEvidence",
          schema: "check");

      migrationBuilder.DropTable(
          name: "HabitExecutions",
          schema: "check");
    }
  }
}
