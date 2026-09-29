using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeparateCourseInstructorsAndExamAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exams_Courses_CourseId",
                table: "Exams");

            migrationBuilder.CreateTable(
                name: "CourseInstructors",
                columns: table => new
                {
                    CourseId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    AssignedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseInstructors", x => new { x.CourseId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CourseInstructors_AspNetUsers_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CourseInstructors_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseInstructors_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseInstructors_AssignedByUserId",
                table: "CourseInstructors",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseInstructors_UserId",
                table: "CourseInstructors",
                column: "UserId");

            migrationBuilder.Sql(
                """
                INSERT INTO "CourseInstructors" ("CourseId", "UserId", "AssignedAtUtc", "AssignedByUserId")
                SELECT "Id", "CreatedByUserId", CURRENT_TIMESTAMP, "CreatedByUserId"
                FROM "Courses"
                WHERE "CreatedByUserId" IS NOT NULL
                ON CONFLICT ("CourseId", "UserId") DO NOTHING;

                DELETE FROM "UserCourses" AS access
                USING "Courses" AS course
                WHERE access."CourseId" = course."Id"
                  AND access."UserId" = course."CreatedByUserId"
                  AND access."AssignmentSource" = 'Creator';
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Exams_Courses_CourseId",
                table: "Exams",
                column: "CourseId",
                principalTable: "Courses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO "UserCourses" ("UserId", "CourseId", "GrantedAtUtc", "PurchasedAtUtc", "AssignmentSource")
                SELECT "UserId", "CourseId", "AssignedAtUtc", NULL, 'Creator'
                FROM "CourseInstructors"
                WHERE "UserId" = "AssignedByUserId"
                ON CONFLICT ("UserId", "CourseId") DO NOTHING;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Exams_Courses_CourseId",
                table: "Exams");

            migrationBuilder.DropTable(
                name: "CourseInstructors");

            migrationBuilder.AddForeignKey(
                name: "FK_Exams_Courses_CourseId",
                table: "Exams",
                column: "CourseId",
                principalTable: "Courses",
                principalColumn: "Id");
        }
    }
}
