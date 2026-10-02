using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExamAssignmentsAndCoursePlacements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAtUtc",
                table: "UserExams",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<string>(
                name: "AssignedByUserId",
                table: "UserExams",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueAtUtc",
                table: "UserExams",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UnlockAtUtc",
                table: "UserExams",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CourseSectionExams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CourseSectionId = table.Column<int>(type: "integer", nullable: false),
                    ExamId = table.Column<int>(type: "integer", nullable: false),
                    OrderNumber = table.Column<int>(type: "integer", nullable: false),
                    UnlockAfterValue = table.Column<int>(type: "integer", nullable: true),
                    UnlockAfterUnit = table.Column<string>(type: "text", nullable: true),
                    IsRequiredForCompletion = table.Column<bool>(type: "boolean", nullable: false),
                    MinimumPassingScore = table.Column<double>(type: "double precision", nullable: false),
                    FailureAction = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseSectionExams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseSectionExams_CourseSections_CourseSectionId",
                        column: x => x.CourseSectionId,
                        principalTable: "CourseSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CourseSectionExams_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourseSectionUserAccesses",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    CourseSectionId = table.Column<int>(type: "integer", nullable: false),
                    UnlockAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsManualOverride = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseSectionUserAccesses", x => new { x.UserId, x.CourseSectionId });
                    table.ForeignKey(
                        name: "FK_CourseSectionUserAccesses_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CourseSectionUserAccesses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CourseSectionUserAccesses_CourseSections_CourseSectionId",
                        column: x => x.CourseSectionId,
                        principalTable: "CourseSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamAttemptGrants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ExamId = table.Column<int>(type: "integer", nullable: false),
                    AdditionalAttempts = table.Column<int>(type: "integer", nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    GrantedByUserId = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamAttemptGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamAttemptGrants_AspNetUsers_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ExamAttemptGrants_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamAttemptGrants_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserExams_AssignedByUserId",
                table: "UserExams",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseSectionExams_CourseSectionId_ExamId",
                table: "CourseSectionExams",
                columns: new[] { "CourseSectionId", "ExamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseSectionExams_CourseSectionId_OrderNumber",
                table: "CourseSectionExams",
                columns: new[] { "CourseSectionId", "OrderNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseSectionExams_ExamId",
                table: "CourseSectionExams",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseSectionUserAccesses_CourseSectionId",
                table: "CourseSectionUserAccesses",
                column: "CourseSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseSectionUserAccesses_UpdatedByUserId",
                table: "CourseSectionUserAccesses",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttemptGrants_ExamId",
                table: "ExamAttemptGrants",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttemptGrants_GrantedByUserId",
                table: "ExamAttemptGrants",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttemptGrants_UserId",
                table: "ExamAttemptGrants",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserExams_AspNetUsers_AssignedByUserId",
                table: "UserExams",
                column: "AssignedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserExams_AspNetUsers_AssignedByUserId",
                table: "UserExams");

            migrationBuilder.DropTable(
                name: "CourseSectionExams");

            migrationBuilder.DropTable(
                name: "CourseSectionUserAccesses");

            migrationBuilder.DropTable(
                name: "ExamAttemptGrants");

            migrationBuilder.DropIndex(
                name: "IX_UserExams_AssignedByUserId",
                table: "UserExams");

            migrationBuilder.DropColumn(
                name: "AssignedAtUtc",
                table: "UserExams");

            migrationBuilder.DropColumn(
                name: "AssignedByUserId",
                table: "UserExams");

            migrationBuilder.DropColumn(
                name: "DueAtUtc",
                table: "UserExams");

            migrationBuilder.DropColumn(
                name: "UnlockAtUtc",
                table: "UserExams");
        }
    }
}
