using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogProductFormFieldBuilderMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowMultipleOptions",
                table: "CatalogProductFormFields",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CharacterLimit",
                table: "CatalogProductFormFields",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ListItemCount",
                table: "CatalogProductFormFields",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowMultipleOptions",
                table: "CatalogProductFormFields");

            migrationBuilder.DropColumn(
                name: "CharacterLimit",
                table: "CatalogProductFormFields");

            migrationBuilder.DropColumn(
                name: "ListItemCount",
                table: "CatalogProductFormFields");
        }
    }
}
