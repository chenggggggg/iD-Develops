using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class SplitCorrectAnswer_Part2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop Old Columns
            migrationBuilder.DropColumn(
                name: "CorrectAnswer",
                table: "TrueOrFalseAnswers");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "CorrectAnswer",
                table: "OpenAnswers");

            migrationBuilder.DropColumn(
                name: "CorrectAnswer",
                table: "MultipleChoiceAnswers");

            migrationBuilder.AlterColumn<double>(
                name: "MaxScore",
                table: "Exams",
                type: "float",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            // Migrate Data from Temporary Columns to New CorrectAnswer Table
            migrationBuilder.Sql(
                @"INSERT INTO CorrectAnswer (Score, QuestionId)
                  SELECT TempScore, Id FROM Questions
                  WHERE TempScore IS NOT NULL;
                  
                  INSERT INTO CorrectAnswer (Text, QuestionId)
                  SELECT TempCorrectAnswer, QuestionId FROM MultipleChoiceAnswers
                  WHERE TempCorrectAnswer IS NOT NULL;

                  INSERT INTO CorrectAnswer (Text, QuestionId)
                  SELECT TempCorrectAnswer, QuestionId FROM OpenAnswers
                  WHERE TempCorrectAnswer IS NOT NULL;

                  INSERT INTO CorrectAnswer (Text, QuestionId)
                  SELECT TempCorrectAnswer, QuestionId FROM TrueOrFalseAnswers
                  WHERE TempCorrectAnswer IS NOT NULL;");

            // Drop Temporary Columns After Migration
            migrationBuilder.DropColumn(
                name: "TempCorrectAnswer",
                table: "TrueOrFalseAnswers");

            migrationBuilder.DropColumn(
                name: "TempCorrectAnswer",
                table: "OpenAnswers");

            migrationBuilder.DropColumn(
                name: "TempCorrectAnswer",
                table: "MultipleChoiceAnswers");

            migrationBuilder.DropColumn(
                name: "TempScore",
                table: "Questions");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorrectAnswer");

            migrationBuilder.AddColumn<string>(
                name: "CorrectAnswer",
                table: "TrueOrFalseAnswers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Score",
                table: "Questions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectAnswer",
                table: "OpenAnswers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CorrectAnswer",
                table: "MultipleChoiceAnswers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<int>(
                name: "MaxScore",
                table: "Exams",
                type: "int",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");
        }
    }
}
