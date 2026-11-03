using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchasedExamEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PurchasedExamEntitlementId",
                table: "Records",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExamAttemptsOnPurchase",
                table: "CatalogProducts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GrantedExamId",
                table: "CatalogProducts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PurchasedExamAttempts",
                table: "CatalogCheckoutAttempts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PurchasedExamId",
                table: "CatalogCheckoutAttempts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PurchasedExamVersionId",
                table: "CatalogCheckoutAttempts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PurchasedExamEntitlements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StripeSessionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CheckoutAttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CatalogProductId = table.Column<int>(type: "integer", nullable: true),
                    ExamId = table.Column<int>(type: "integer", nullable: false),
                    ExamVersionId = table.Column<int>(type: "integer", nullable: false),
                    TotalAttempts = table.Column<int>(type: "integer", nullable: false),
                    UsedAttempts = table.Column<int>(type: "integer", nullable: false),
                    PurchasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchasedExamEntitlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchasedExamEntitlements_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchasedExamEntitlements_CatalogProducts_CatalogProductId",
                        column: x => x.CatalogProductId,
                        principalTable: "CatalogProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PurchasedExamEntitlements_ExamVersions_ExamVersionId",
                        column: x => x.ExamVersionId,
                        principalTable: "ExamVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchasedExamEntitlements_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Records_PurchasedExamEntitlementId",
                table: "Records",
                column: "PurchasedExamEntitlementId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProducts_GrantedExamId",
                table: "CatalogProducts",
                column: "GrantedExamId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogCheckoutAttempts_PurchasedExamVersionId",
                table: "CatalogCheckoutAttempts",
                column: "PurchasedExamVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchasedExamEntitlements_CatalogProductId",
                table: "PurchasedExamEntitlements",
                column: "CatalogProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchasedExamEntitlements_ExamId",
                table: "PurchasedExamEntitlements",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchasedExamEntitlements_ExamVersionId",
                table: "PurchasedExamEntitlements",
                column: "ExamVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchasedExamEntitlements_StripeSessionId",
                table: "PurchasedExamEntitlements",
                column: "StripeSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchasedExamEntitlements_UserId_ExamId",
                table: "PurchasedExamEntitlements",
                columns: new[] { "UserId", "ExamId" });

            migrationBuilder.AddForeignKey(
                name: "FK_CatalogCheckoutAttempts_ExamVersions_PurchasedExamVersionId",
                table: "CatalogCheckoutAttempts",
                column: "PurchasedExamVersionId",
                principalTable: "ExamVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CatalogProducts_Exams_GrantedExamId",
                table: "CatalogProducts",
                column: "GrantedExamId",
                principalTable: "Exams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Records_PurchasedExamEntitlements_PurchasedExamEntitlementId",
                table: "Records",
                column: "PurchasedExamEntitlementId",
                principalTable: "PurchasedExamEntitlements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CatalogCheckoutAttempts_ExamVersions_PurchasedExamVersionId",
                table: "CatalogCheckoutAttempts");

            migrationBuilder.DropForeignKey(
                name: "FK_CatalogProducts_Exams_GrantedExamId",
                table: "CatalogProducts");

            migrationBuilder.DropForeignKey(
                name: "FK_Records_PurchasedExamEntitlements_PurchasedExamEntitlementId",
                table: "Records");

            migrationBuilder.DropTable(
                name: "PurchasedExamEntitlements");

            migrationBuilder.DropIndex(
                name: "IX_Records_PurchasedExamEntitlementId",
                table: "Records");

            migrationBuilder.DropIndex(
                name: "IX_CatalogProducts_GrantedExamId",
                table: "CatalogProducts");

            migrationBuilder.DropIndex(
                name: "IX_CatalogCheckoutAttempts_PurchasedExamVersionId",
                table: "CatalogCheckoutAttempts");

            migrationBuilder.DropColumn(
                name: "PurchasedExamEntitlementId",
                table: "Records");

            migrationBuilder.DropColumn(
                name: "ExamAttemptsOnPurchase",
                table: "CatalogProducts");

            migrationBuilder.DropColumn(
                name: "GrantedExamId",
                table: "CatalogProducts");

            migrationBuilder.DropColumn(
                name: "PurchasedExamAttempts",
                table: "CatalogCheckoutAttempts");

            migrationBuilder.DropColumn(
                name: "PurchasedExamId",
                table: "CatalogCheckoutAttempts");

            migrationBuilder.DropColumn(
                name: "PurchasedExamVersionId",
                table: "CatalogCheckoutAttempts");
        }
    }
}
