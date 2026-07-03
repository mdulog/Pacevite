using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pacevite.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStravaSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalActivityId",
                table: "Events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncConnectionId",
                table: "Events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SyncConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Platform = table.Column<string>(type: "text", nullable: false),
                    ExternalAthleteId = table.Column<string>(type: "text", nullable: false),
                    AccessTokenEncrypted = table.Column<string>(type: "text", nullable: false),
                    RefreshTokenEncrypted = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncConnections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_SyncConnectionId",
                table: "Events",
                column: "SyncConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncConnections_UserId_Platform",
                table: "SyncConnections",
                columns: new[] { "UserId", "Platform" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Events_SyncConnections_SyncConnectionId",
                table: "Events",
                column: "SyncConnectionId",
                principalTable: "SyncConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_SyncConnections_SyncConnectionId",
                table: "Events");

            migrationBuilder.DropTable(
                name: "SyncConnections");

            migrationBuilder.DropIndex(
                name: "IX_Events_SyncConnectionId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "ExternalActivityId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "SyncConnectionId",
                table: "Events");
        }
    }
}
