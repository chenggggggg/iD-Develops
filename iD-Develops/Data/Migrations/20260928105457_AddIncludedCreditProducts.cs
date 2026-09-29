using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncludedCreditProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogProductIncludedCreditProducts",
                columns: table => new
                {
                    CatalogProductId = table.Column<int>(type: "integer", nullable: false),
                    IncludedCreditProductId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogProductIncludedCreditProducts", x => new { x.CatalogProductId, x.IncludedCreditProductId });
                    table.ForeignKey(
                        name: "FK_CatalogProductIncludedCreditProducts_CatalogProducts_Catalo~",
                        column: x => x.CatalogProductId,
                        principalTable: "CatalogProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CatalogProductIncludedCreditProducts_CatalogProducts_Includ~",
                        column: x => x.IncludedCreditProductId,
                        principalTable: "CatalogProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProductIncludedCreditProducts_IncludedCreditProductId",
                table: "CatalogProductIncludedCreditProducts",
                column: "IncludedCreditProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogProductIncludedCreditProducts");
        }
    }
}
