using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class UpdateQuestionsAndAnswers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MultipleChoiceQuestions");

            migrationBuilder.DropTable(
                name: "OpenQuestions");

            migrationBuilder.DropTable(
                name: "TrueOrFalseQuestions");

            migrationBuilder.DropColumn(
                name: "ParticipantAnswer",
                table: "TrueOrFalseAnswers");

            migrationBuilder.DropColumn(
                name: "ParticipantAnswer",
                table: "OpenAnswers");

            migrationBuilder.DropColumn(
                name: "ParticipantAnswer",
                table: "MultipleChoiceAnswers");

            migrationBuilder.RenameColumn(
                name: "QuestionText",
                table: "Questions",
                newName: "Type");

            migrationBuilder.RenameColumn(
                name: "QuestionScenario",
                table: "Questions",
                newName: "Scenario");

            migrationBuilder.AddColumn<int>(
                name: "QuestionId",
                table: "TrueOrFalseAnswers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Text",
                table: "Questions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "QuestionId",
                table: "OpenAnswers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "QuestionId",
                table: "MultipleChoiceAnswers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ParticipantAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnswerText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AnswerDataType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    QuestionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParticipantAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParticipantAnswers_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrueOrFalseAnswers_QuestionId",
                table: "TrueOrFalseAnswers",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenAnswers_QuestionId",
                table: "OpenAnswers",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_MultipleChoiceAnswers_QuestionId",
                table: "MultipleChoiceAnswers",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantAnswers_QuestionId",
                table: "ParticipantAnswers",
                column: "QuestionId");

            migrationBuilder.AddForeignKey(
                name: "FK_MultipleChoiceAnswers_Questions_QuestionId",
                table: "MultipleChoiceAnswers",
                column: "QuestionId",
                principalTable: "Questions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OpenAnswers_Questions_QuestionId",
                table: "OpenAnswers",
                column: "QuestionId",
                principalTable: "Questions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TrueOrFalseAnswers_Questions_QuestionId",
                table: "TrueOrFalseAnswers",
                column: "QuestionId",
                principalTable: "Questions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MultipleChoiceAnswers_Questions_QuestionId",
                table: "MultipleChoiceAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_OpenAnswers_Questions_QuestionId",
                table: "OpenAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_TrueOrFalseAnswers_Questions_QuestionId",
                table: "TrueOrFalseAnswers");

            migrationBuilder.DropTable(
                name: "ParticipantAnswers");

            migrationBuilder.DropIndex(
                name: "IX_TrueOrFalseAnswers_QuestionId",
                table: "TrueOrFalseAnswers");

            migrationBuilder.DropIndex(
                name: "IX_OpenAnswers_QuestionId",
                table: "OpenAnswers");

            migrationBuilder.DropIndex(
                name: "IX_MultipleChoiceAnswers_QuestionId",
                table: "MultipleChoiceAnswers");

            migrationBuilder.DropColumn(
                name: "QuestionId",
                table: "TrueOrFalseAnswers");

            migrationBuilder.DropColumn(
                name: "Text",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "QuestionId",
                table: "OpenAnswers");

            migrationBuilder.DropColumn(
                name: "QuestionId",
                table: "MultipleChoiceAnswers");

            migrationBuilder.RenameColumn(
                name: "Type",
                table: "Questions",
                newName: "QuestionText");

            migrationBuilder.RenameColumn(
                name: "Scenario",
                table: "Questions",
                newName: "QuestionScenario");

            migrationBuilder.AddColumn<bool>(
                name: "ParticipantAnswer",
                table: "TrueOrFalseAnswers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ParticipantAnswer",
                table: "OpenAnswers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ParticipantAnswer",
                table: "MultipleChoiceAnswers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "MultipleChoiceQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    AnswerId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MultipleChoiceQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MultipleChoiceQuestions_MultipleChoiceAnswers_AnswerId",
                        column: x => x.AnswerId,
                        principalTable: "MultipleChoiceAnswers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MultipleChoiceQuestions_Questions_Id",
                        column: x => x.Id,
                        principalTable: "Questions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OpenQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    AnswerId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenQuestions_OpenAnswers_AnswerId",
                        column: x => x.AnswerId,
                        principalTable: "OpenAnswers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OpenQuestions_Questions_Id",
                        column: x => x.Id,
                        principalTable: "Questions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TrueOrFalseQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    AnswerId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrueOrFalseQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrueOrFalseQuestions_Questions_Id",
                        column: x => x.Id,
                        principalTable: "Questions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TrueOrFalseQuestions_TrueOrFalseAnswers_AnswerId",
                        column: x => x.AnswerId,
                        principalTable: "TrueOrFalseAnswers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MultipleChoiceQuestions_AnswerId",
                table: "MultipleChoiceQuestions",
                column: "AnswerId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenQuestions_AnswerId",
                table: "OpenQuestions",
                column: "AnswerId");

            migrationBuilder.CreateIndex(
                name: "IX_TrueOrFalseQuestions_AnswerId",
                table: "TrueOrFalseQuestions",
                column: "AnswerId");
        }
    }
}
