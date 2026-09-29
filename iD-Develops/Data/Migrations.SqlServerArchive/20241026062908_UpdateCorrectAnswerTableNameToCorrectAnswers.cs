using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class UpdateCorrectAnswerTableNameToCorrectAnswers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CorrectAnswer_Questions_QuestionId",
                table: "CorrectAnswer");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CorrectAnswer",
                table: "CorrectAnswer");

            migrationBuilder.RenameTable(
                name: "CorrectAnswer",
                newName: "CorrectAnswers");

            migrationBuilder.RenameIndex(
                name: "IX_CorrectAnswer_QuestionId",
                table: "CorrectAnswers",
                newName: "IX_CorrectAnswers_QuestionId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CorrectAnswers",
                table: "CorrectAnswers",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CorrectAnswers_Questions_QuestionId",
                table: "CorrectAnswers",
                column: "QuestionId",
                principalTable: "Questions",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CorrectAnswers_Questions_QuestionId",
                table: "CorrectAnswers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CorrectAnswers",
                table: "CorrectAnswers");

            migrationBuilder.RenameTable(
                name: "CorrectAnswers",
                newName: "CorrectAnswer");

            migrationBuilder.RenameIndex(
                name: "IX_CorrectAnswers_QuestionId",
                table: "CorrectAnswer",
                newName: "IX_CorrectAnswer_QuestionId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CorrectAnswer",
                table: "CorrectAnswer",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CorrectAnswer_Questions_QuestionId",
                table: "CorrectAnswer",
                column: "QuestionId",
                principalTable: "Questions",
                principalColumn: "Id");
        }
    }
}
