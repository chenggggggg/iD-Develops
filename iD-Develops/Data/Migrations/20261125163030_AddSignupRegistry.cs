using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSignupRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CatalogProductId",
                table: "ProductFormSubmissions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerName",
                table: "ProductFormSubmissions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductSlug",
                table: "ProductFormSubmissions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Recover only data actually stored with the signup. A matching current
            // slug or name cannot prove a historical product identity after deletion
            // or reuse, so CatalogProductId deliberately remains null for old rows.
            migrationBuilder.Sql("""
                DO $$
                DECLARE submission record; payload jsonb;
                BEGIN
                    FOR submission IN
                        SELECT "Id", "SubmissionDataJson" FROM "ProductFormSubmissions"
                        WHERE "CustomerName" IS NULL AND "ProductSlug" IS NULL
                    LOOP
                        BEGIN
                            payload := submission."SubmissionDataJson"::jsonb;
                        EXCEPTION WHEN data_exception THEN
                            CONTINUE;
                        END;
                        IF jsonb_typeof(payload) IS DISTINCT FROM 'object' THEN CONTINUE; END IF;
                        UPDATE "ProductFormSubmissions"
                        SET "CustomerName" = NULLIF(LEFT(COALESCE(
                            CASE WHEN jsonb_typeof(payload->'signup_full_name') = 'string' THEN payload->>'signup_full_name' END,
                            CASE WHEN jsonb_typeof(payload->'customer_name') = 'string' THEN payload->>'customer_name' END,
                            CASE WHEN jsonb_typeof(payload->'full_name') = 'string' THEN payload->>'full_name' END,
                            CASE WHEN jsonb_typeof(payload->'name') = 'string' THEN payload->>'name' END), 200), ''),
                            "ProductSlug" = CASE WHEN jsonb_typeof(payload->'product_slug') = 'string'
                                THEN NULLIF(LEFT(payload->>'product_slug', 200), '') END
                        WHERE "Id" = submission."Id";
                    END LOOP;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ProductFormSubmissions_CatalogProductId_SubmissionDate_Id",
                table: "ProductFormSubmissions",
                columns: new[] { "CatalogProductId", "SubmissionDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductFormSubmissions_ProductSlug_SubmissionDate",
                table: "ProductFormSubmissions",
                columns: new[] { "ProductSlug", "SubmissionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductFormSubmissions_SubmissionDate_Id",
                table: "ProductFormSubmissions",
                columns: new[] { "SubmissionDate", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductFormSubmissions_CatalogProductId_SubmissionDate_Id",
                table: "ProductFormSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_ProductFormSubmissions_ProductSlug_SubmissionDate",
                table: "ProductFormSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_ProductFormSubmissions_SubmissionDate_Id",
                table: "ProductFormSubmissions");

            migrationBuilder.DropColumn(
                name: "CatalogProductId",
                table: "ProductFormSubmissions");

            migrationBuilder.DropColumn(
                name: "CustomerName",
                table: "ProductFormSubmissions");

            migrationBuilder.DropColumn(
                name: "ProductSlug",
                table: "ProductFormSubmissions");
        }
    }
}
