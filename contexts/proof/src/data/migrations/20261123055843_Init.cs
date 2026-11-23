using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ctx.Proof.data.migrations
{
  /// <inheritdoc />
  public partial class Init : Migration
  {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.EnsureSchema(
          name: "proof");

      migrationBuilder.CreateTable(
          name: "NfcTags",
          schema: "proof",
          columns: table => new
          {
            Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            HabitId = table.Column<Guid>(type: "uuid", nullable: false),
            ClaimedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_NfcTags", x => x.Id);
          });

      migrationBuilder.CreateTable(
          name: "ProofBindings",
          schema: "proof",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            HabitId = table.Column<Guid>(type: "uuid", nullable: false),
            PluginKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            PluginVersion = table.Column<int>(type: "integer", nullable: false),
            Settings = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_ProofBindings", x => x.Id);
          });

      migrationBuilder.CreateTable(
          name: "StravaConnections",
          schema: "proof",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            AthleteId = table.Column<long>(type: "bigint", nullable: false),
            AccessToken = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
            RefreshToken = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
            ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            Scope = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
            RefreshLeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            ConnectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_StravaConnections", x => x.Id);
          });

      migrationBuilder.CreateTable(
          name: "StravaEvents",
          schema: "proof",
          columns: table => new
          {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            ObjectType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
            AspectType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
            ObjectId = table.Column<long>(type: "bigint", nullable: false),
            OwnerId = table.Column<long>(type: "bigint", nullable: false),
            EventTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            Deauthorized = table.Column<bool>(type: "boolean", nullable: false),
            Attempts = table.Column<int>(type: "integer", nullable: false),
            ClaimedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
          },
          constraints: table =>
          {
            table.PrimaryKey("PK_StravaEvents", x => x.Id);
          });

      migrationBuilder.CreateIndex(
          name: "IX_NfcTags_UserId",
          schema: "proof",
          table: "NfcTags",
          column: "UserId");

      migrationBuilder.CreateIndex(
          name: "IX_ProofBindings_HabitId",
          schema: "proof",
          table: "ProofBindings",
          column: "HabitId",
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_ProofBindings_UserId_PluginKey",
          schema: "proof",
          table: "ProofBindings",
          columns: new[] { "UserId", "PluginKey" });

      migrationBuilder.CreateIndex(
          name: "IX_StravaConnections_AthleteId",
          schema: "proof",
          table: "StravaConnections",
          column: "AthleteId",
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_StravaConnections_UserId",
          schema: "proof",
          table: "StravaConnections",
          column: "UserId",
          unique: true);

      migrationBuilder.CreateIndex(
          name: "IX_StravaEvents_OwnerId",
          schema: "proof",
          table: "StravaEvents",
          column: "OwnerId");

      migrationBuilder.CreateIndex(
          name: "IX_StravaEvents_ReceivedAtUtc",
          schema: "proof",
          table: "StravaEvents",
          column: "ReceivedAtUtc");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
      migrationBuilder.DropTable(
          name: "NfcTags",
          schema: "proof");

      migrationBuilder.DropTable(
          name: "ProofBindings",
          schema: "proof");

      migrationBuilder.DropTable(
          name: "StravaConnections",
          schema: "proof");

      migrationBuilder.DropTable(
          name: "StravaEvents",
          schema: "proof");
    }
  }
}
