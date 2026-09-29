using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionScoringAndPersistRecordScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "Score",
                table: "Records",
                type: "float",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<double>(
                name: "Score",
                table: "Questions",
                type: "float",
                nullable: true,
                defaultValue: 1d);

            migrationBuilder.AddColumn<double>(
                name: "Score",
                table: "ExamVersionQuestions",
                type: "float",
                nullable: true,
                defaultValue: 1d);

            migrationBuilder.Sql(
                @"UPDATE Questions
                  SET Score = 1
                  WHERE Score IS NULL;

                  UPDATE ExamVersionQuestions
                  SET Score = 1
                  WHERE Score IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Score",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "ExamVersionQuestions");

            migrationBuilder.AlterColumn<int>(
                name: "Score",
                table: "Records",
                type: "int",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");
        }
    }
}
