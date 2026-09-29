using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExamResultGrading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResultGradeCustomTextPrimary",
                table: "Exams",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultGradeCustomTextSecondary",
                table: "Exams",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResultGradeDisplayMode",
                table: "Exams",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ResultGradeCustomTextPrimary",
                table: "ExamVersions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultGradeCustomTextSecondary",
                table: "ExamVersions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResultGradeDisplayMode",
                table: "ExamVersions",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResultGradeCustomTextPrimary",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "ResultGradeCustomTextSecondary",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "ResultGradeDisplayMode",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "ResultGradeCustomTextPrimary",
                table: "ExamVersions");

            migrationBuilder.DropColumn(
                name: "ResultGradeCustomTextSecondary",
                table: "ExamVersions");

            migrationBuilder.DropColumn(
                name: "ResultGradeDisplayMode",
                table: "ExamVersions");
        }
    }
}
