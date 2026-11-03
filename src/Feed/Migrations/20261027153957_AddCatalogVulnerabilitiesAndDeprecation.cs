using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NuGet.Feed.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogVulnerabilitiesAndDeprecation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AlternatePackageId",
                table: "Packages",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AlternatePackageVersionRange",
                table: "Packages",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeprecationMessage",
                table: "Packages",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeprecationReasons",
                table: "Packages",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRepositorySigned",
                table: "Packages",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LicenseExpression",
                table: "Packages",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackageHash",
                table: "Packages",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PackageSize",
                table: "Packages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "CatalogEntries",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CommitId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CommitTimestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PackageId = table.Column<string>(type: "TEXT COLLATE NOCASE", maxLength: 128, nullable: false),
                    PackageVersion = table.Column<string>(type: "TEXT COLLATE NOCASE", maxLength: 64, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogEntries", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "VulnerabilityAdvisories",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PackageId = table.Column<string>(type: "TEXT COLLATE NOCASE", maxLength: 128, nullable: false),
                    VersionRange = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AdvisoryUrl = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    Updated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VulnerabilityAdvisories", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogEntries_CommitTimestamp",
                table: "CatalogEntries",
                column: "CommitTimestamp");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogEntries_PackageId_PackageVersion",
                table: "CatalogEntries",
                columns: new[] { "PackageId", "PackageVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_VulnerabilityAdvisories_PackageId_AdvisoryUrl",
                table: "VulnerabilityAdvisories",
                columns: new[] { "PackageId", "AdvisoryUrl" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogEntries");

            migrationBuilder.DropTable(
                name: "VulnerabilityAdvisories");

            migrationBuilder.DropColumn(
                name: "AlternatePackageId",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "AlternatePackageVersionRange",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "DeprecationMessage",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "DeprecationReasons",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "IsRepositorySigned",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "LicenseExpression",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "PackageHash",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "PackageSize",
                table: "Packages");
        }
    }
}
