using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogProductHomepagePresentation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HomepageButtonText",
                table: "CatalogProductTranslations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HomepageButtonText",
                table: "CatalogProducts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHomepageHighlighted",
                table: "CatalogProducts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HomepageButtonText",
                table: "CatalogProductTranslations");

            migrationBuilder.DropColumn(
                name: "HomepageButtonText",
                table: "CatalogProducts");

            migrationBuilder.DropColumn(
                name: "IsHomepageHighlighted",
                table: "CatalogProducts");
        }
    }
}
