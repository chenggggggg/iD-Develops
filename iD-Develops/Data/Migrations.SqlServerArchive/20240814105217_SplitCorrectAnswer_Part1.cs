using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class SplitCorrectAnswer_Part1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create New CorrectAnswer Table
            migrationBuilder.CreateTable(
                name: "CorrectAnswer",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Score = table.Column<double>(type: "float", nullable: true),
                    QuestionId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrectAnswer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CorrectAnswer_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CorrectAnswer_QuestionId",
                table: "CorrectAnswer",
                column: "QuestionId");

            // Add Temporary Columns
            migrationBuilder.AddColumn<string>(
                name: "TempCorrectAnswer",
                table: "TrueOrFalseAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TempCorrectAnswer",
                table: "OpenAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TempCorrectAnswer",
                table: "MultipleChoiceAnswers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TempScore",
                table: "Questions",
                type: "int",
                nullable: true);

            // Migrate Data to Temporary Columns
            migrationBuilder.Sql(
                @"UPDATE MultipleChoiceAnswers SET TempCorrectAnswer = CorrectAnswer;
                  UPDATE OpenAnswers SET TempCorrectAnswer = CorrectAnswer;
                  UPDATE TrueOrFalseAnswers SET TempCorrectAnswer = CorrectAnswer;
                  UPDATE Questions SET TempScore = Score;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorrectAnswer");

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
    }
}
