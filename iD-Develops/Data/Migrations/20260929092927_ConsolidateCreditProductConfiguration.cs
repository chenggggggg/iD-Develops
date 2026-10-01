using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateCreditProductConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CreditConsumptionPolicyId",
                table: "CatalogProducts",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(
                """
                INSERT INTO "CreditConsumptionPolicies" (
                    "Name",
                    "NormalizedName",
                    "Description",
                    "ConsumptionTiming",
                    "CancellationWindowHours",
                    "AttendedAction",
                    "NoShowAction",
                    "EarlyCancellationAction",
                    "LateCancellationAction",
                    "StaffCancellationAction",
                    "IsActive",
                    "CreatedAtUtc",
                    "UpdatedAtUtc")
                SELECT
                    LEFT(product."Name" || ' booking rules', 150),
                    'CREDIT-PRODUCT-' || product."Id",
                    'Booking and cancellation rules managed by this Credit Product.',
                    COALESCE(existing_policy."ConsumptionTiming", 'OnBooking'),
                    COALESCE(existing_policy."CancellationWindowHours", 24),
                    COALESCE(existing_policy."AttendedAction", 'Consume'),
                    COALESCE(existing_policy."NoShowAction", 'Consume'),
                    COALESCE(existing_policy."EarlyCancellationAction", 'Return'),
                    COALESCE(existing_policy."LateCancellationAction", 'Consume'),
                    COALESCE(existing_policy."StaffCancellationAction", 'Return'),
                    TRUE,
                    NOW(),
                    NOW()
                FROM "CatalogProducts" AS product
                LEFT JOIN LATERAL (
                    SELECT policy.*
                    FROM "CatalogProductCreditGrants" AS grant_item
                    JOIN "CourseClasses" AS course_class
                        ON course_class."RequiredCreditTypeId" = grant_item."CreditTypeId"
                    JOIN "CreditConsumptionPolicies" AS policy
                        ON policy."Id" = course_class."CreditConsumptionPolicyId"
                    WHERE grant_item."CatalogProductId" = product."Id"
                    ORDER BY course_class."Id"
                    LIMIT 1
                ) AS existing_policy ON TRUE
                WHERE product."ProductType" = 'Credit'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "CreditConsumptionPolicies" AS existing
                      WHERE existing."NormalizedName" = 'CREDIT-PRODUCT-' || product."Id");

                UPDATE "CatalogProducts" AS product
                SET "CreditConsumptionPolicyId" = policy."Id",
                    "RequiresAccountCreation" = TRUE
                FROM "CreditConsumptionPolicies" AS policy
                WHERE product."ProductType" = 'Credit'
                  AND policy."NormalizedName" = 'CREDIT-PRODUCT-' || product."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProducts_CreditConsumptionPolicyId",
                table: "CatalogProducts",
                column: "CreditConsumptionPolicyId");

            migrationBuilder.AddForeignKey(
                name: "FK_CatalogProducts_CreditConsumptionPolicies_CreditConsumption~",
                table: "CatalogProducts",
                column: "CreditConsumptionPolicyId",
                principalTable: "CreditConsumptionPolicies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CatalogProducts_CreditConsumptionPolicies_CreditConsumption~",
                table: "CatalogProducts");

            migrationBuilder.DropIndex(
                name: "IX_CatalogProducts_CreditConsumptionPolicyId",
                table: "CatalogProducts");

            migrationBuilder.DropColumn(
                name: "CreditConsumptionPolicyId",
                table: "CatalogProducts");
        }
    }
}
