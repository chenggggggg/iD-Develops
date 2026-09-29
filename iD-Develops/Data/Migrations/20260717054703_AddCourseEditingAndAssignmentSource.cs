using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseEditingAndAssignmentSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Lectures_CourseSectionId",
                table: "Lectures");

            migrationBuilder.AddColumn<string>(
                name: "AssignmentSource",
                table: "UserCourses",
                type: "text",
                nullable: false,
                defaultValue: "Admin");

            migrationBuilder.AddColumn<int>(
                name: "OrderNumber",
                table: "Lectures",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE "UserCourses" AS access
                SET "AssignmentSource" = 'Creator'
                FROM "Courses" AS course
                WHERE access."CourseId" = course."Id"
                  AND access."UserId" = course."CreatedByUserId";

                UPDATE "UserCourses"
                SET "AssignmentSource" = 'Purchase'
                WHERE "PurchasedAtUtc" IS NOT NULL;

                WITH ranked AS (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "CourseSectionId"
                               ORDER BY "Id")::integer AS "Position"
                    FROM "Lectures"
                )
                UPDATE "Lectures" AS lecture
                SET "OrderNumber" = ranked."Position"
                FROM ranked
                WHERE lecture."Id" = ranked."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Lectures_CourseSectionId_OrderNumber",
                table: "Lectures",
                columns: new[] { "CourseSectionId", "OrderNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Lectures_CourseSectionId_OrderNumber",
                table: "Lectures");

            migrationBuilder.DropColumn(
                name: "AssignmentSource",
                table: "UserCourses");

            migrationBuilder.DropColumn(
                name: "OrderNumber",
                table: "Lectures");

            migrationBuilder.CreateIndex(
                name: "IX_Lectures_CourseSectionId",
                table: "Lectures",
                column: "CourseSectionId");
        }
    }
}
