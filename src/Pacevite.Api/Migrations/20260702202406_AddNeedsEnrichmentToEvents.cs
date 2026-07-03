using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pacevite.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNeedsEnrichmentToEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NeedsEnrichment",
                table: "Events",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NeedsEnrichment",
                table: "Events");
        }
    }
}
