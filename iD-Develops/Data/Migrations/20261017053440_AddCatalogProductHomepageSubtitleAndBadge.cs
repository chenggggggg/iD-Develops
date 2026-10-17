using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogProductHomepageSubtitleAndBadge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HomepageHighlightText",
                table: "CatalogProductTranslations",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HomepageSubtitle",
                table: "CatalogProductTranslations",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HomepageHighlightText",
                table: "CatalogProducts",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HomepageSubtitle",
                table: "CatalogProducts",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HomepageHighlightText",
                table: "CatalogProductTranslations");

            migrationBuilder.DropColumn(
                name: "HomepageSubtitle",
                table: "CatalogProductTranslations");

            migrationBuilder.DropColumn(
                name: "HomepageHighlightText",
                table: "CatalogProducts");

            migrationBuilder.DropColumn(
                name: "HomepageSubtitle",
                table: "CatalogProducts");
        }
    }
}
