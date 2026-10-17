using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogProductHomepageContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HomepageDescription",
                table: "CatalogProductTranslations",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HomepageHighlights",
                table: "CatalogProductTranslations",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HomepageDescription",
                table: "CatalogProducts",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HomepageHighlights",
                table: "CatalogProducts",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HomepageDescription",
                table: "CatalogProductTranslations");

            migrationBuilder.DropColumn(
                name: "HomepageHighlights",
                table: "CatalogProductTranslations");

            migrationBuilder.DropColumn(
                name: "HomepageDescription",
                table: "CatalogProducts");

            migrationBuilder.DropColumn(
                name: "HomepageHighlights",
                table: "CatalogProducts");
        }
    }
}
