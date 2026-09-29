using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260328115500_AddCatalogProductSalesActive")]
    public partial class AddCatalogProductSalesActive : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSalesActive",
                table: "CatalogProducts",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSalesActive",
                table: "CatalogProducts");
        }
    }
}
