using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NuDepot.Feed.Migrations
{
    /// <inheritdoc />
    public partial class RequirePackageDependencyPackage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dependencies without a package can't be reached, and can't be kept now that each needs one. SQLite can't
            // change a column or a foreign key, so EF Core rebuilds the table, with each dependency deleted with its
            // package.
            migrationBuilder.Sql("DELETE FROM \"PackageDependencies\" WHERE \"PackageKey\" IS NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_PackageDependencies_Packages_PackageKey",
                table: "PackageDependencies");

            migrationBuilder.AlterColumn<int>(
                name: "PackageKey",
                table: "PackageDependencies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PackageDependencies_Packages_PackageKey",
                table: "PackageDependencies",
                column: "PackageKey",
                principalTable: "Packages",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PackageDependencies_Packages_PackageKey",
                table: "PackageDependencies");

            migrationBuilder.AlterColumn<int>(
                name: "PackageKey",
                table: "PackageDependencies",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddForeignKey(
                name: "FK_PackageDependencies_Packages_PackageKey",
                table: "PackageDependencies",
                column: "PackageKey",
                principalTable: "Packages",
                principalColumn: "Key");
        }
    }
}
