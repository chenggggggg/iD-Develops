using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogCheckoutAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CatalogProductId = table.Column<int>(type: "integer", nullable: false),
                    VariantId = table.Column<int>(type: "integer", nullable: false),
                    AuthenticatedUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CustomerEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    FormDataJson = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    ParticipantCount = table.Column<int>(type: "integer", nullable: false),
                    InviteUseId = table.Column<int>(type: "integer", nullable: true),
                    AmountTotalMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    StripeSessionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CheckoutUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogCheckoutAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogCheckoutAttempts_CatalogProducts_CatalogProductId",
                        column: x => x.CatalogProductId,
                        principalTable: "CatalogProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogCheckoutMails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StripeSessionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Recipient = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ModelJson = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogCheckoutMails", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogCheckoutReceipts",
                columns: table => new
                {
                    StripeSessionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogCheckoutReceipts", x => x.StripeSessionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogCheckoutAttempts_CatalogProductId",
                table: "CatalogCheckoutAttempts",
                column: "CatalogProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogCheckoutAttempts_ExpiresAtUtc",
                table: "CatalogCheckoutAttempts",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogCheckoutAttempts_RequestKey",
                table: "CatalogCheckoutAttempts",
                column: "RequestKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogCheckoutAttempts_StripeSessionId",
                table: "CatalogCheckoutAttempts",
                column: "StripeSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogCheckoutMails_SentAtUtc_NextAttemptAtUtc",
                table: "CatalogCheckoutMails",
                columns: new[] { "SentAtUtc", "NextAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogCheckoutAttempts");

            migrationBuilder.DropTable(
                name: "CatalogCheckoutMails");

            migrationBuilder.DropTable(
                name: "CatalogCheckoutReceipts");
        }
    }
}
