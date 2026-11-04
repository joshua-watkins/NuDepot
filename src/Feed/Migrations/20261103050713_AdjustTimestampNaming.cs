using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NuDepot.Feed.Migrations
{
    /// <inheritdoc />
    public partial class AdjustTimestampNaming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Updated",
                table: "VulnerabilityAdvisories",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "Published",
                table: "Packages",
                newName: "PublishedAt");

            migrationBuilder.RenameColumn(
                name: "CommitTimestamp",
                table: "CatalogEntries",
                newName: "CommittedAt");

            migrationBuilder.RenameIndex(
                name: "IX_CatalogEntries_CommitTimestamp",
                table: "CatalogEntries",
                newName: "IX_CatalogEntries_CommittedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "VulnerabilityAdvisories",
                newName: "Updated");

            migrationBuilder.RenameColumn(
                name: "PublishedAt",
                table: "Packages",
                newName: "Published");

            migrationBuilder.RenameColumn(
                name: "CommittedAt",
                table: "CatalogEntries",
                newName: "CommitTimestamp");

            migrationBuilder.RenameIndex(
                name: "IX_CatalogEntries_CommittedAt",
                table: "CatalogEntries",
                newName: "IX_CatalogEntries_CommitTimestamp");
        }
    }
}
