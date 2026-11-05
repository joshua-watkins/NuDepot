using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NuDepot.Feed.Migrations
{
    /// <inheritdoc />
    public partial class AddGeneratedSigningCertificates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RepositorySignatureExpiresAt",
                table: "Packages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepositorySignatureFingerprint",
                table: "Packages",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FriendlyName = table.Column<string>(type: "TEXT", nullable: true),
                    Xml = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SigningCertificates",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Certificate = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ProtectedPrivateKey = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SigningCertificates", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SigningCertificates_Fingerprint",
                table: "SigningCertificates",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SigningCertificates_Year",
                table: "SigningCertificates",
                column: "Year");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "SigningCertificates");

            migrationBuilder.DropColumn(
                name: "RepositorySignatureExpiresAt",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "RepositorySignatureFingerprint",
                table: "Packages");
        }
    }
}
