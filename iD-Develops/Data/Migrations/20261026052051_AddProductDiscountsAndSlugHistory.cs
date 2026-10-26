using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductDiscountsAndSlugHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiscountType",
                table: "CatalogProducts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountValue",
                table: "CatalogProducts",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CatalogProductSlug",
                columns: table => new
                {
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CatalogProductId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogProductSlug", x => x.Slug);
                    table.ForeignKey(
                        name: "FK_CatalogProductSlug_CatalogProducts_CatalogProductId",
                        column: x => x.CatalogProductId,
                        principalTable: "CatalogProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProductSlug_CatalogProductId",
                table: "CatalogProductSlug",
                column: "CatalogProductId");

            migrationBuilder.Sql("""
                INSERT INTO "CatalogProductSlug" ("Slug", "CatalogProductId")
                SELECT "Slug", "Id" FROM "CatalogProducts";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogProductSlug");

            migrationBuilder.DropColumn(
                name: "DiscountType",
                table: "CatalogProducts");

            migrationBuilder.DropColumn(
                name: "DiscountValue",
                table: "CatalogProducts");
        }
    }
}
