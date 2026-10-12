using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class UseSecondBasedExamTimeLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TimeLimit",
                table: "ExamVersions",
                newName: "TimeLimitSeconds");

            migrationBuilder.RenameColumn(
                name: "TimeLimit",
                table: "Exams",
                newName: "TimeLimitSeconds");

            migrationBuilder.Sql(
                """
                UPDATE "ExamVersions"
                SET "TimeLimitSeconds" = "TimeLimitSeconds" * 60
                WHERE "TimeLimitSeconds" IS NOT NULL;

                UPDATE "Exams"
                SET "TimeLimitSeconds" = "TimeLimitSeconds" * 60
                WHERE "TimeLimitSeconds" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "ExamVersions"
                SET "TimeLimitSeconds" = "TimeLimitSeconds" / 60
                WHERE "TimeLimitSeconds" IS NOT NULL;

                UPDATE "Exams"
                SET "TimeLimitSeconds" = "TimeLimitSeconds" / 60
                WHERE "TimeLimitSeconds" IS NOT NULL;
                """);

            migrationBuilder.RenameColumn(
                name: "TimeLimitSeconds",
                table: "ExamVersions",
                newName: "TimeLimit");

            migrationBuilder.RenameColumn(
                name: "TimeLimitSeconds",
                table: "Exams",
                newName: "TimeLimit");
        }
    }
}
