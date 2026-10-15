using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogProductTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogProductFormFieldTranslations",
                columns: table => new
                {
                    CatalogProductFormFieldId = table.Column<int>(type: "integer", nullable: false),
                    Culture = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Label = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Placeholder = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    HelpText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    OptionsText = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogProductFormFieldTranslations", x => new { x.CatalogProductFormFieldId, x.Culture });
                    table.ForeignKey(
                        name: "FK_CatalogProductFormFieldTranslations_CatalogProductFormField~",
                        column: x => x.CatalogProductFormFieldId,
                        principalTable: "CatalogProductFormFields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogProductTranslations",
                columns: table => new
                {
                    CatalogProductId = table.Column<int>(type: "integer", nullable: false),
                    Culture = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    FullDescriptionHtml = table.Column<string>(type: "text", nullable: true),
                    ExternalBookingButtonText = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ConfirmationEmailSubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ConfirmationEmailBodyHtml = table.Column<string>(type: "text", nullable: true),
                    OwnerNotificationSubject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OwnerNotificationBodyHtml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogProductTranslations", x => new { x.CatalogProductId, x.Culture });
                    table.ForeignKey(
                        name: "FK_CatalogProductTranslations_CatalogProducts_CatalogProductId",
                        column: x => x.CatalogProductId,
                        principalTable: "CatalogProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogProductVariantTranslations",
                columns: table => new
                {
                    CatalogProductVariantId = table.Column<int>(type: "integer", nullable: false),
                    Culture = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogProductVariantTranslations", x => new { x.CatalogProductVariantId, x.Culture });
                    table.ForeignKey(
                        name: "FK_CatalogProductVariantTranslations_CatalogProductVariants_Ca~",
                        column: x => x.CatalogProductVariantId,
                        principalTable: "CatalogProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogProductFormFieldTranslations");

            migrationBuilder.DropTable(
                name: "CatalogProductTranslations");

            migrationBuilder.DropTable(
                name: "CatalogProductVariantTranslations");
        }
    }
}
