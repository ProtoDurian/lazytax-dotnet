using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ctx.Payment.data.migrations
{
  /// <inheritdoc />
  public partial class Init : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.EnsureSchema(
          name: "payment");

      migrationBuilder.CreateTable(
          name: "Charges",
          schema: "payment",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            Purpose = table.Column<int>(type: "integer", nullable: false),
            AmountCents = table.Column<long>(type: "bigint", nullable: false),
            Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
            Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
            State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
            Attempt = table.Column<int>(type: "integer", nullable: false),
            IntentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
            GatewayStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
            LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
            InFlightUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_Charges", x => x.Id);
          });

      migrationBuilder.CreateTable(
          name: "PaymentCustomers",
          schema: "payment",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            AirwallexCustomerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_PaymentCustomers", x => x.Id);
          });

      migrationBuilder.CreateTable(
          name: "PaymentConsents",
          schema: "payment",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            Purpose = table.Column<int>(type: "integer", nullable: false),
            ConsentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
            CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            PaymentCustomerId = table.Column<Guid>(type: "uuid", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_PaymentConsents", x => x.Id);
            table.ForeignKey(
                      name: "FK_PaymentConsents_PaymentCustomers_PaymentCustomerId",
                      column: x => x.PaymentCustomerId,
                      principalSchema: "payment",
                      principalTable: "PaymentCustomers",
                      principalColumn: "Id",
                      onDelete: ReferentialAction.Cascade);
          });

      migrationBuilder.CreateIndex(
          name: "IX_Charges_IntentId",
          schema: "payment",
          table: "Charges",
          column: "IntentId");

      migrationBuilder.CreateIndex(
          name: "IX_Charges_UserId",
          schema: "payment",
          table: "Charges",
          column: "UserId");

      migrationBuilder.CreateIndex(
          name: "IX_PaymentConsents_PaymentCustomerId_Purpose",
          schema: "payment",
          table: "PaymentConsents",
          columns: ["PaymentCustomerId", "Purpose"],
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_PaymentCustomers_AirwallexCustomerId",
          schema: "payment",
          table: "PaymentCustomers",
          column: "AirwallexCustomerId",
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_PaymentCustomers_UserId",
          schema: "payment",
          table: "PaymentCustomers",
          column: "UserId",
          unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(
          name: "Charges",
          schema: "payment");

      migrationBuilder.DropTable(
          name: "PaymentConsents",
          schema: "payment");

      migrationBuilder.DropTable(
          name: "PaymentCustomers",
          schema: "payment");
    }
  }
}
