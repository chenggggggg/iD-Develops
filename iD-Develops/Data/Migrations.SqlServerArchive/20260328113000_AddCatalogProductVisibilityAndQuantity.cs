using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260328113000_AddCatalogProductVisibilityAndQuantity")]
    public partial class AddCatalogProductVisibilityAndQuantity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableQuantity",
                table: "CatalogProducts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HideFromProductsPage",
                table: "CatalogProducts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxQuantity",
                table: "CatalogProducts",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "MinQuantity",
                table: "CatalogProducts",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableQuantity",
                table: "CatalogProducts");

            migrationBuilder.DropColumn(
                name: "HideFromProductsPage",
                table: "CatalogProducts");

            migrationBuilder.DropColumn(
                name: "MaxQuantity",
                table: "CatalogProducts");

            migrationBuilder.DropColumn(
                name: "MinQuantity",
                table: "CatalogProducts");
        }
    }
}
