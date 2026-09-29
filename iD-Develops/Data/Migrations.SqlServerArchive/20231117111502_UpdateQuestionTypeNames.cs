using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class UpdateQuestionTypeNames : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.DropPrimaryKey(
                name: "PK_TrueOrFalseAnswer",
                table: "TrueOrFalseAnswer");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OpenAnswer",
                table: "OpenAnswer");

            migrationBuilder.DropPrimaryKey(
                name: "PK_MultipleChoiceAnswer",
                table: "MultipleChoiceAnswer");

            migrationBuilder.RenameTable(
                name: "TrueOrFalseAnswer",
                newName: "TrueOrFalseAnswers");

            migrationBuilder.RenameTable(
                name: "OpenAnswer",
                newName: "OpenAnswers");

            migrationBuilder.RenameTable(
                name: "MultipleChoiceAnswer",
                newName: "MultipleChoiceAnswers");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TrueOrFalseAnswers",
                table: "TrueOrFalseAnswers",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OpenAnswers",
                table: "OpenAnswers",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MultipleChoiceAnswers",
                table: "MultipleChoiceAnswers",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_MultipleChoiceQuestions_MultipleChoiceAnswers_AnswerId",
                table: "MultipleChoiceQuestions",
                column: "AnswerId",
                principalTable: "MultipleChoiceAnswers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OpenQuestions_OpenAnswers_AnswerId",
                table: "OpenQuestions",
                column: "AnswerId",
                principalTable: "OpenAnswers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TrueOrFalseQuestions_TrueOrFalseAnswers_AnswerId",
                table: "TrueOrFalseQuestions",
                column: "AnswerId",
                principalTable: "TrueOrFalseAnswers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MultipleChoiceQuestions_MultipleChoiceAnswers_AnswerId",
                table: "MultipleChoiceQuestions");

            migrationBuilder.DropForeignKey(
                name: "FK_OpenQuestions_OpenAnswers_AnswerId",
                table: "OpenQuestions");

            migrationBuilder.DropForeignKey(
                name: "FK_TrueOrFalseQuestions_TrueOrFalseAnswers_AnswerId",
                table: "TrueOrFalseQuestions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TrueOrFalseAnswers",
                table: "TrueOrFalseAnswers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OpenAnswers",
                table: "OpenAnswers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_MultipleChoiceAnswers",
                table: "MultipleChoiceAnswers");

            migrationBuilder.RenameTable(
                name: "TrueOrFalseAnswers",
                newName: "TrueOrFalseAnswer");

            migrationBuilder.RenameTable(
                name: "OpenAnswers",
                newName: "OpenAnswer");

            migrationBuilder.RenameTable(
                name: "MultipleChoiceAnswers",
                newName: "MultipleChoiceAnswer");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TrueOrFalseAnswer",
                table: "TrueOrFalseAnswer",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OpenAnswer",
                table: "OpenAnswer",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MultipleChoiceAnswer",
                table: "MultipleChoiceAnswer",
                column: "Id");

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
    }
}
