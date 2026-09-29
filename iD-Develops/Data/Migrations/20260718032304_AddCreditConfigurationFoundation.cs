using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditConfigurationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BookingAccess",
                table: "CourseClasses",
                type: "text",
                nullable: false,
                defaultValue: "CourseEnrollment");

            migrationBuilder.AddColumn<int>(
                name: "Capacity",
                table: "CourseClasses",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CreditConsumptionPolicyId",
                table: "CourseClasses",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreditCost",
                table: "CourseClasses",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                table: "CourseClasses",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<string>(
                name: "Format",
                table: "CourseClasses",
                type: "text",
                nullable: false,
                defaultValue: "Group");

            migrationBuilder.AddColumn<int>(
                name: "RequiredCreditTypeId",
                table: "CourseClasses",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CreditConsumptionPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ConsumptionTiming = table.Column<string>(type: "text", nullable: false),
                    CancellationWindowHours = table.Column<int>(type: "integer", nullable: false),
                    AttendedAction = table.Column<string>(type: "text", nullable: false),
                    NoShowAction = table.Column<string>(type: "text", nullable: false),
                    EarlyCancellationAction = table.Column<string>(type: "text", nullable: false),
                    LateCancellationAction = table.Column<string>(type: "text", nullable: false),
                    StaffCancellationAction = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditConsumptionPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CreditTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SingularLabel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PluralLabel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DefaultValidityValue = table.Column<int>(type: "integer", nullable: true),
                    DefaultValidityUnit = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogProductCreditGrants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CatalogProductId = table.Column<int>(type: "integer", nullable: false),
                    CreditTypeId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    ValidityValue = table.Column<int>(type: "integer", nullable: true),
                    ValidityUnit = table.Column<string>(type: "text", nullable: true),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    CourseId = table.Column<int>(type: "integer", nullable: true),
                    CourseClassId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogProductCreditGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogProductCreditGrants_CatalogProducts_CatalogProductId",
                        column: x => x.CatalogProductId,
                        principalTable: "CatalogProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CatalogProductCreditGrants_CourseClasses_CourseClassId",
                        column: x => x.CourseClassId,
                        principalTable: "CourseClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CatalogProductCreditGrants_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CatalogProductCreditGrants_CreditTypes_CreditTypeId",
                        column: x => x.CreditTypeId,
                        principalTable: "CreditTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseClasses_CreditConsumptionPolicyId",
                table: "CourseClasses",
                column: "CreditConsumptionPolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseClasses_RequiredCreditTypeId",
                table: "CourseClasses",
                column: "RequiredCreditTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProductCreditGrants_CatalogProductId_CreditTypeId_C~1",
                table: "CatalogProductCreditGrants",
                columns: new[] { "CatalogProductId", "CreditTypeId", "CourseId" },
                unique: true,
                filter: "\"CourseId\" IS NOT NULL AND \"CourseClassId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProductCreditGrants_CatalogProductId_CreditTypeId_Co~",
                table: "CatalogProductCreditGrants",
                columns: new[] { "CatalogProductId", "CreditTypeId", "CourseClassId" },
                unique: true,
                filter: "\"CourseClassId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProductCreditGrants_CatalogProductId_CreditTypeId_Sc~",
                table: "CatalogProductCreditGrants",
                columns: new[] { "CatalogProductId", "CreditTypeId", "Scope" },
                unique: true,
                filter: "\"Scope\" = 'Global'");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProductCreditGrants_CourseClassId",
                table: "CatalogProductCreditGrants",
                column: "CourseClassId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProductCreditGrants_CourseId",
                table: "CatalogProductCreditGrants",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProductCreditGrants_CreditTypeId",
                table: "CatalogProductCreditGrants",
                column: "CreditTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditConsumptionPolicies_NormalizedName",
                table: "CreditConsumptionPolicies",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditTypes_NormalizedName",
                table: "CreditTypes",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CourseClasses_CreditConsumptionPolicies_CreditConsumptionPo~",
                table: "CourseClasses",
                column: "CreditConsumptionPolicyId",
                principalTable: "CreditConsumptionPolicies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CourseClasses_CreditTypes_RequiredCreditTypeId",
                table: "CourseClasses",
                column: "RequiredCreditTypeId",
                principalTable: "CreditTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CourseClasses_CreditConsumptionPolicies_CreditConsumptionPo~",
                table: "CourseClasses");

            migrationBuilder.DropForeignKey(
                name: "FK_CourseClasses_CreditTypes_RequiredCreditTypeId",
                table: "CourseClasses");

            migrationBuilder.DropTable(
                name: "CatalogProductCreditGrants");

            migrationBuilder.DropTable(
                name: "CreditConsumptionPolicies");

            migrationBuilder.DropTable(
                name: "CreditTypes");

            migrationBuilder.DropIndex(
                name: "IX_CourseClasses_CreditConsumptionPolicyId",
                table: "CourseClasses");

            migrationBuilder.DropIndex(
                name: "IX_CourseClasses_RequiredCreditTypeId",
                table: "CourseClasses");

            migrationBuilder.DropColumn(
                name: "BookingAccess",
                table: "CourseClasses");

            migrationBuilder.DropColumn(
                name: "Capacity",
                table: "CourseClasses");

            migrationBuilder.DropColumn(
                name: "CreditConsumptionPolicyId",
                table: "CourseClasses");

            migrationBuilder.DropColumn(
                name: "CreditCost",
                table: "CourseClasses");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "CourseClasses");

            migrationBuilder.DropColumn(
                name: "Format",
                table: "CourseClasses");

            migrationBuilder.DropColumn(
                name: "RequiredCreditTypeId",
                table: "CourseClasses");
        }
    }
}
