using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NuDepot.Feed.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionSigningKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SessionSigningKeys",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    KeyId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ProtectedPrivateKey = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionSigningKeys", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionSigningKeys_KeyId",
                table: "SessionSigningKeys",
                column: "KeyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionSigningKeys");
        }
    }
}
