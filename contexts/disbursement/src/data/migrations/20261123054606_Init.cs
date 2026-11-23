using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ctx.Disbursement.data.migrations
{
  /// <inheritdoc />
  public partial class Init : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.EnsureSchema(
          name: "disbursement");

      migrationBuilder.CreateTable(
          name: "Disbursements",
          schema: "disbursement",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            CharityId = table.Column<Guid>(type: "uuid", nullable: false),
            Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
            AmountCents = table.Column<long>(type: "bigint", nullable: false),
            Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
            PledgeOrganizationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
            ProviderDonationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
            Attempts = table.Column<int>(type: "integer", nullable: false),
            LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
            CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_Disbursements", x => x.Id);
          });

      migrationBuilder.CreateIndex(
          name: "IX_Disbursements_CharityId",
          schema: "disbursement",
          table: "Disbursements",
          column: "CharityId");

      migrationBuilder.CreateIndex(
          name: "IX_Disbursements_Status_UpdatedAt",
          schema: "disbursement",
          table: "Disbursements",
          columns: new[] { "Status", "UpdatedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(
          name: "Disbursements",
          schema: "disbursement");
    }
  }
}
