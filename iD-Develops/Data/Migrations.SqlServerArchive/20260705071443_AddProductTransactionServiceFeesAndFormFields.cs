using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductTransactionServiceFeesAndFormFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MaterialsFee",
                table: "CatalogProducts",
                newName: "TransactionFee");

            migrationBuilder.AddColumn<decimal>(
                name: "ServiceFee",
                table: "CatalogProducts",
                type: "decimal(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ServiceFee",
                table: "CatalogProducts");

            migrationBuilder.RenameColumn(
                name: "TransactionFee",
                table: "CatalogProducts",
                newName: "MaterialsFee");
        }
    }
}
