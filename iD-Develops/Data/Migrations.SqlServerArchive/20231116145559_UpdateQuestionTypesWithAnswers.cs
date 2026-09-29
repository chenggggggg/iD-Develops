using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class UpdateQuestionTypesWithAnswers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CorrectAnswer",
                table: "TrueOrFalseQuestions");

            migrationBuilder.DropColumn(
                name: "ParticipantAnswer",
                table: "TrueOrFalseQuestions");

            migrationBuilder.DropColumn(
                name: "CorrectAnswer",
                table: "OpenQuestions");

            migrationBuilder.DropColumn(
                name: "ParticipantAnswer",
                table: "OpenQuestions");

            migrationBuilder.DropColumn(
                name: "AnswerA",
                table: "MultipleChoiceQuestions");

            migrationBuilder.DropColumn(
                name: "AnswerB",
                table: "MultipleChoiceQuestions");

            migrationBuilder.DropColumn(
                name: "AnswerC",
                table: "MultipleChoiceQuestions");

            migrationBuilder.DropColumn(
                name: "AnswerD",
                table: "MultipleChoiceQuestions");

            migrationBuilder.DropColumn(
                name: "AnswerE",
                table: "MultipleChoiceQuestions");

            migrationBuilder.DropColumn(
                name: "CorrectAnswer",
                table: "MultipleChoiceQuestions");

            migrationBuilder.DropColumn(
                name: "ParticipantAnswer",
                table: "MultipleChoiceQuestions");

            migrationBuilder.AddColumn<int>(
                name: "AnswerId",
                table: "TrueOrFalseQuestions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AnswerId",
                table: "OpenQuestions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AnswerId",
                table: "MultipleChoiceQuestions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MultipleChoiceAnswer",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CorrectAnswer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ParticipantAnswer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AnswerA = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AnswerB = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AnswerC = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AnswerD = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AnswerE = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MultipleChoiceAnswer", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpenAnswer",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CorrectAnswer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ParticipantAnswer = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenAnswer", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrueOrFalseAnswer",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CorrectAnswer = table.Column<bool>(type: "bit", nullable: false),
                    ParticipantAnswer = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrueOrFalseAnswer", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrueOrFalseQuestions_AnswerId",
                table: "TrueOrFalseQuestions",
                column: "AnswerId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenQuestions_AnswerId",
                table: "OpenQuestions",
                column: "AnswerId");

            migrationBuilder.CreateIndex(
                name: "IX_MultipleChoiceQuestions_AnswerId",
                table: "MultipleChoiceQuestions",
                column: "AnswerId");

            migrationBuilder.AddForeignKey(
                name: "FK_MultipleChoiceQuestions_MultipleChoiceAnswer_AnswerId",
                table: "MultipleChoiceQuestions",
                column: "AnswerId",
                principalTable: "MultipleChoiceAnswer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OpenQuestions_OpenAnswer_AnswerId",
                table: "OpenQuestions",
                column: "AnswerId",
                principalTable: "OpenAnswer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TrueOrFalseQuestions_TrueOrFalseAnswer_AnswerId",
                table: "TrueOrFalseQuestions",
                column: "AnswerId",
                principalTable: "TrueOrFalseAnswer",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MultipleChoiceQuestions_MultipleChoiceAnswer_AnswerId",
                table: "MultipleChoiceQuestions");

            migrationBuilder.DropForeignKey(
                name: "FK_OpenQuestions_OpenAnswer_AnswerId",
                table: "OpenQuestions");

            migrationBuilder.DropForeignKey(
                name: "FK_TrueOrFalseQuestions_TrueOrFalseAnswer_AnswerId",
                table: "TrueOrFalseQuestions");

            migrationBuilder.DropTable(
                name: "MultipleChoiceAnswer");

            migrationBuilder.DropTable(
                name: "OpenAnswer");

            migrationBuilder.DropTable(
                name: "TrueOrFalseAnswer");

            migrationBuilder.DropIndex(
                name: "IX_TrueOrFalseQuestions_AnswerId",
                table: "TrueOrFalseQuestions");

            migrationBuilder.DropIndex(
                name: "IX_OpenQuestions_AnswerId",
                table: "OpenQuestions");

            migrationBuilder.DropIndex(
                name: "IX_MultipleChoiceQuestions_AnswerId",
                table: "MultipleChoiceQuestions");

            migrationBuilder.DropColumn(
                name: "AnswerId",
                table: "TrueOrFalseQuestions");

            migrationBuilder.DropColumn(
                name: "AnswerId",
                table: "OpenQuestions");

            migrationBuilder.DropColumn(
                name: "AnswerId",
                table: "MultipleChoiceQuestions");

            migrationBuilder.AddColumn<bool>(
                name: "CorrectAnswer",
                table: "TrueOrFalseQuestions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ParticipantAnswer",
                table: "TrueOrFalseQuestions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CorrectAnswer",
                table: "OpenQuestions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ParticipantAnswer",
                table: "OpenQuestions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AnswerA",
                table: "MultipleChoiceQuestions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AnswerB",
                table: "MultipleChoiceQuestions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AnswerC",
                table: "MultipleChoiceQuestions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AnswerD",
                table: "MultipleChoiceQuestions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AnswerE",
                table: "MultipleChoiceQuestions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CorrectAnswer",
                table: "MultipleChoiceQuestions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ParticipantAnswer",
                table: "MultipleChoiceQuestions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
