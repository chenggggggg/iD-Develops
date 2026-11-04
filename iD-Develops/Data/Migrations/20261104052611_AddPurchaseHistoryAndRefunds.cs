using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseHistoryAndRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RefundBlocked",
                table: "UserCreditLots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RefundRevokedQuantity",
                table: "UserCreditLots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RefundBlocked",
                table: "PurchasedExamEntitlements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ProductNameSnapshot",
                table: "CatalogCheckoutAttempts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CatalogPurchases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StripeSessionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PaymentIntentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CheckoutAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CatalogProductId = table.Column<int>(type: "integer", nullable: true),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    AmountMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    IsLocal = table.Column<bool>(type: "boolean", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FulfilledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HasUntrackedBenefits = table.Column<bool>(type: "boolean", nullable: false),
                    ExamEntitlementCount = table.Column<int>(type: "integer", nullable: false),
                    CreditLotCount = table.Column<int>(type: "integer", nullable: false),
                    GrantedCourseId = table.Column<int>(type: "integer", nullable: true),
                    OwnedCourseAccessGrantedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefundBlocked = table.Column<bool>(type: "boolean", nullable: false),
                    RefundedMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    RefundStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogPurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogPurchases_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CatalogPurchases_CatalogProducts_CatalogProductId",
                        column: x => x.CatalogProductId,
                        principalTable: "CatalogProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CatalogRefunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogPurchaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderRefundId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AmountMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    IsExternal = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextCheckAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogRefunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogRefunds_CatalogPurchases_CatalogPurchaseId",
                        column: x => x.CatalogPurchaseId,
                        principalTable: "CatalogPurchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogPurchases_CatalogProductId",
                table: "CatalogPurchases",
                column: "CatalogProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogPurchases_PaymentIntentId",
                table: "CatalogPurchases",
                column: "PaymentIntentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogPurchases_StripeSessionId",
                table: "CatalogPurchases",
                column: "StripeSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogPurchases_UserId_PaidAtUtc",
                table: "CatalogPurchases",
                columns: new[] { "UserId", "PaidAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogRefunds_CatalogPurchaseId",
                table: "CatalogRefunds",
                column: "CatalogPurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogRefunds_ProviderRefundId",
                table: "CatalogRefunds",
                column: "ProviderRefundId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogRefunds_Status_NextCheckAtUtc",
                table: "CatalogRefunds",
                columns: new[] { "Status", "NextCheckAtUtc" });

            // Import only explicit account references; an email match is not purchase ownership.
            // Earlier deliveries have incomplete benefit snapshots and require manual refund review.
            migrationBuilder.Sql("""
                INSERT INTO "CatalogPurchases"
                    ("Id", "StripeSessionId", "PaymentIntentId", "CheckoutAttemptId", "UserId", "CatalogProductId", "ProductName",
                     "Currency", "AmountMinorUnits", "Quantity", "IsLocal", "PaidAtUtc", "FulfilledAtUtc", "HasUntrackedBenefits",
                     "ExamEntitlementCount", "CreditLotCount", "RefundBlocked", "RefundedMinorUnits", "RefundStatus")
                SELECT gen_random_uuid(), a."StripeSessionId",
                    CASE WHEN a."StripeSessionId" LIKE 'local\_%' ESCAPE '\' THEN 'local_payment_' || a."StripeSessionId"
                    ELSE (SELECT e."MetadataJson"->>'stripePaymentIntentId' FROM "ApplicationAuditEvents" e
                          WHERE e."MetadataJson"->>'stripeSessionId' = a."StripeSessionId"
                            AND e."MetadataJson"->>'stripePaymentIntentId' IS NOT NULL ORDER BY e."Id" DESC LIMIT 1) END,
                    a."Id", u."Id", a."CatalogProductId", COALESCE(a."ProductNameSnapshot", p."Name"),
                    lower(a."Currency"), a."AmountTotalMinorUnits", a."Quantity", a."StripeSessionId" LIKE 'local\_%' ESCAPE '\',
                    a."CompletedAtUtc", a."CompletedAtUtc", TRUE, 0, 0, FALSE, 0, 'None'
                FROM "CatalogCheckoutAttempts" a
                JOIN "CatalogCheckoutReceipts" r ON r."StripeSessionId" = a."StripeSessionId"
                JOIN "CatalogProducts" p ON p."Id" = a."CatalogProductId"
                LEFT JOIN "AspNetUsers" u ON u."Id" = a."AuthenticatedUserId"
                WHERE a."CompletedAtUtc" IS NOT NULL
                ON CONFLICT DO NOTHING;

                INSERT INTO "CatalogPurchases"
                    ("Id", "StripeSessionId", "PaymentIntentId", "UserId", "CatalogProductId", "ProductName", "Currency",
                     "AmountMinorUnits", "Quantity", "IsLocal", "PaidAtUtc", "FulfilledAtUtc", "HasUntrackedBenefits",
                     "ExamEntitlementCount", "CreditLotCount", "RefundBlocked", "RefundedMinorUnits", "RefundStatus")
                SELECT gen_random_uuid(), e."MetadataJson"->>'stripeSessionId', e."MetadataJson"->>'stripePaymentIntentId',
                    u."Id", p."Id", left(COALESCE(e."MetadataJson"->>'productName', p."Name", 'Purchased product'), 200),
                    lower(e."MetadataJson"->>'currency'), (e."MetadataJson"->>'amountTotalMinorUnits')::bigint,
                    CASE WHEN e."MetadataJson"->>'quantity' ~ '^[1-9][0-9]{0,4}$' THEN (e."MetadataJson"->>'quantity')::int ELSE 1 END,
                    FALSE, e."OccurredAtUtc", e."OccurredAtUtc", TRUE, 0, 0, FALSE, 0, 'None'
                FROM "ApplicationAuditEvents" e
                JOIN "AspNetUsers" u ON u."Id" = e."MetadataJson"->>'userId'
                LEFT JOIN "CatalogProducts" p ON p."Id" = CASE WHEN e."MetadataJson"->>'productId' ~ '^[0-9]{1,9}$'
                    THEN (e."MetadataJson"->>'productId')::int ELSE NULL END
                WHERE e."EventType" = 'commerce.purchase.completed'
                    AND length(e."MetadataJson"->>'stripeSessionId') BETWEEN 1 AND 200
                    AND e."MetadataJson"->>'amountTotalMinorUnits' ~ '^[0-9]{1,15}$'
                    AND e."MetadataJson"->>'currency' ~ '^[A-Za-z]{3}$'
                    AND NOT EXISTS (SELECT 1 FROM "CatalogPurchases" existing WHERE existing."StripeSessionId" = e."MetadataJson"->>'stripeSessionId')
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogRefunds");

            migrationBuilder.DropTable(
                name: "CatalogPurchases");

            migrationBuilder.DropColumn(
                name: "RefundBlocked",
                table: "UserCreditLots");

            migrationBuilder.DropColumn(
                name: "RefundRevokedQuantity",
                table: "UserCreditLots");

            migrationBuilder.DropColumn(
                name: "RefundBlocked",
                table: "PurchasedExamEntitlements");

            migrationBuilder.DropColumn(
                name: "ProductNameSnapshot",
                table: "CatalogCheckoutAttempts");
        }
    }
}
