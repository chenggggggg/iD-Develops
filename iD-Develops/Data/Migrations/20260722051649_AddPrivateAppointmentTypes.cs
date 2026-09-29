using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateAppointmentTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScheduledEvents_TeacherUserId",
                table: "ScheduledEvents");

            migrationBuilder.AddColumn<int>(
                name: "AppointmentTypeId",
                table: "ScheduledEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppointmentTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    RequiredCreditTypeId = table.Column<int>(type: "integer", nullable: false),
                    CreditCost = table.Column<int>(type: "integer", nullable: false),
                    CreditConsumptionPolicyId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentTypes_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentTypes_CreditConsumptionPolicies_CreditConsumptio~",
                        column: x => x.CreditConsumptionPolicyId,
                        principalTable: "CreditConsumptionPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentTypes_CreditTypes_RequiredCreditTypeId",
                        column: x => x.RequiredCreditTypeId,
                        principalTable: "CreditTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppointmentTypeTeachers",
                columns: table => new
                {
                    AppointmentTypeId = table.Column<int>(type: "integer", nullable: false),
                    TeacherUserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentTypeTeachers", x => new { x.AppointmentTypeId, x.TeacherUserId });
                    table.ForeignKey(
                        name: "FK_AppointmentTypeTeachers_AppointmentTypes_AppointmentTypeId",
                        column: x => x.AppointmentTypeId,
                        principalTable: "AppointmentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppointmentTypeTeachers_AspNetUsers_TeacherUserId",
                        column: x => x.TeacherUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_AppointmentTypeId",
                table: "ScheduledEvents",
                column: "AppointmentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_TeacherUserId_StartAtUtc_EndAtUtc",
                table: "ScheduledEvents",
                columns: new[] { "TeacherUserId", "StartAtUtc", "EndAtUtc" },
                unique: true,
                filter: "\"Status\" = 'Scheduled'");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentTypes_CreatedByUserId",
                table: "AppointmentTypes",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentTypes_CreditConsumptionPolicyId",
                table: "AppointmentTypes",
                column: "CreditConsumptionPolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentTypes_RequiredCreditTypeId",
                table: "AppointmentTypes",
                column: "RequiredCreditTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentTypeTeachers_TeacherUserId",
                table: "AppointmentTypeTeachers",
                column: "TeacherUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ScheduledEvents_AppointmentTypes_AppointmentTypeId",
                table: "ScheduledEvents",
                column: "AppointmentTypeId",
                principalTable: "AppointmentTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScheduledEvents_AppointmentTypes_AppointmentTypeId",
                table: "ScheduledEvents");

            migrationBuilder.DropTable(
                name: "AppointmentTypeTeachers");

            migrationBuilder.DropTable(
                name: "AppointmentTypes");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledEvents_AppointmentTypeId",
                table: "ScheduledEvents");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledEvents_TeacherUserId_StartAtUtc_EndAtUtc",
                table: "ScheduledEvents");

            migrationBuilder.DropColumn(
                name: "AppointmentTypeId",
                table: "ScheduledEvents");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_TeacherUserId",
                table: "ScheduledEvents",
                column: "TeacherUserId");
        }
    }
}
