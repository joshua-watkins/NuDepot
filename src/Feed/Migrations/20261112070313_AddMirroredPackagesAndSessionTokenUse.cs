using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NuDepot.Feed.Migrations
{
    /// <inheritdoc />
    public partial class AddMirroredPackagesAndSessionTokenUse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing sessions' tokens were already sent, so a copy of an older cookie still revokes them.
            migrationBuilder.AddColumn<bool>(
                name: "IsTokenUsed",
                table: "UserSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsMirrored",
                table: "Packages",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Where existing packages came from wasn't recorded. Those with another repository's signature, like
            // nuget.org's, were most likely mirrored; the rest are taken to have been pushed.
            migrationBuilder.Sql(
                "UPDATE \"Packages\" SET \"IsMirrored\" = 1 " +
                "WHERE \"RepositorySignatureFingerprint\" IS NOT NULL AND \"IsRepositorySigned\" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTokenUsed",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "IsMirrored",
                table: "Packages");
        }
    }
}
