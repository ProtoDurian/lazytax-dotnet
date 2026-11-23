using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ctx.Account.data.migrations
{
  /// <inheritdoc />
  public partial class Init : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.EnsureSchema(
          name: "account");

      migrationBuilder.CreateTable(
          name: "Configurations",
          schema: "account",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            Timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            EndOfDay = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
            DefaultCharityId = table.Column<Guid>(type: "uuid", nullable: true)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_Configurations", x => x.Id);
          });

      migrationBuilder.CreateTable(
          name: "Users",
          schema: "account",
          columns: table => new
          {
            Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            Username = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
            Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
            EmailVerified = table.Column<bool>(type: "boolean", nullable: false),
            Scopes = table.Column<string[]>(type: "text[]", nullable: false),
            Active = table.Column<bool>(type: "boolean", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_Users", x => x.Id);
          });

      migrationBuilder.CreateIndex(
          name: "IX_Configurations_UserId",
          schema: "account",
          table: "Configurations",
          column: "UserId",
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_Users_Username",
          schema: "account",
          table: "Users",
          column: "Username",
          unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(
          name: "Configurations",
          schema: "account");

      migrationBuilder.DropTable(
          name: "Users",
          schema: "account");
    }
  }
}
