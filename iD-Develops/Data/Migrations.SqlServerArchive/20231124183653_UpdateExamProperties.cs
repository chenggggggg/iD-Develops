using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class UpdateExamProperties : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnswerE",
                table: "MultipleChoiceAnswers");

            migrationBuilder.AddColumn<string>(
                name: "IntroductionDutch",
                table: "Exams",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "IntroductionEnglish",
                table: "Exams",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsIntakeExam",
                table: "Exams",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TimeLimit",
                table: "Exams",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IntroductionDutch",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "IntroductionEnglish",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "IsIntakeExam",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "TimeLimit",
                table: "Exams");

            migrationBuilder.AddColumn<string>(
                name: "AnswerE",
                table: "MultipleChoiceAnswers",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
