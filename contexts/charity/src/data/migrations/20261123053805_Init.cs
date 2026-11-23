using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ctx.Charity.data.migrations
{
  /// <inheritdoc />
  public partial class Init : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.EnsureSchema(
          name: "charity");

      migrationBuilder.CreateTable(
          name: "Causes",
          schema: "charity",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_Causes", x => x.Id);
          });

      migrationBuilder.CreateTable(
          name: "Charities",
          schema: "charity",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
            Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
            Mission = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
            Countries = table.Column<string[]>(type: "text[]", nullable: false),
            PrimaryRegistrationNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
            PrimaryRegistrationCountry = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
            WebsiteUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
            LogoUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_Charities", x => x.Id);
          });

      migrationBuilder.CreateTable(
          name: "CharityCauses",
          schema: "charity",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            CharityId = table.Column<Guid>(type: "uuid", nullable: false),
            CauseId = table.Column<Guid>(type: "uuid", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_CharityCauses", x => x.Id);
            table.ForeignKey(
                      name: "FK_CharityCauses_Causes_CauseId",
                      column: x => x.CauseId,
                      principalSchema: "charity",
                      principalTable: "Causes",
                      principalColumn: "Id",
                      onDelete: ReferentialAction.Cascade);
            table.ForeignKey(
                      name: "FK_CharityCauses_Charities_CharityId",
                      column: x => x.CharityId,
                      principalSchema: "charity",
                      principalTable: "Charities",
                      principalColumn: "Id",
                      onDelete: ReferentialAction.Cascade);
          });

      migrationBuilder.CreateTable(
          name: "ExternalIds",
          schema: "charity",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            CharityId = table.Column<Guid>(type: "uuid", nullable: false),
            Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            ExternalKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
            Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
            Payload = table.Column<string>(type: "text", nullable: true),
            LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_ExternalIds", x => x.Id);
            table.ForeignKey(
                      name: "FK_ExternalIds_Charities_CharityId",
                      column: x => x.CharityId,
                      principalSchema: "charity",
                      principalTable: "Charities",
                      principalColumn: "Id",
                      onDelete: ReferentialAction.Cascade);
          });

      migrationBuilder.CreateIndex(
          name: "IX_Causes_Key",
          schema: "charity",
          table: "Causes",
          column: "Key",
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_Charities_Countries",
          schema: "charity",
          table: "Charities",
          column: "Countries")
          .Annotation("Npgsql:IndexMethod", "gin");

      migrationBuilder.CreateIndex(
          name: "IX_Charities_Name",
          schema: "charity",
          table: "Charities",
          column: "Name");

      migrationBuilder.CreateIndex(
          name: "IX_Charities_PrimaryRegistrationCountry_PrimaryRegistrationNum~",
          schema: "charity",
          table: "Charities",
          columns: ["PrimaryRegistrationCountry", "PrimaryRegistrationNumber"]);

      migrationBuilder.CreateIndex(
          name: "IX_CharityCauses_CauseId",
          schema: "charity",
          table: "CharityCauses",
          column: "CauseId");

      migrationBuilder.CreateIndex(
          name: "IX_CharityCauses_CharityId_CauseId",
          schema: "charity",
          table: "CharityCauses",
          columns: ["CharityId", "CauseId"],
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_ExternalIds_CharityId",
          schema: "charity",
          table: "ExternalIds",
          column: "CharityId");

      migrationBuilder.CreateIndex(
          name: "IX_ExternalIds_Source_ExternalKey",
          schema: "charity",
          table: "ExternalIds",
          columns: ["Source", "ExternalKey"],
          unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(
          name: "CharityCauses",
          schema: "charity");

      migrationBuilder.DropTable(
          name: "ExternalIds",
          schema: "charity");

      migrationBuilder.DropTable(
          name: "Causes",
          schema: "charity");

      migrationBuilder.DropTable(
          name: "Charities",
          schema: "charity");
    }
  }
}
