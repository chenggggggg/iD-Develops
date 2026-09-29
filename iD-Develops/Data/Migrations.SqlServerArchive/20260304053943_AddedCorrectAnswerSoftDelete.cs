using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class AddedCorrectAnswerSoftDelete : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CorrectAnswers_Questions_QuestionId",
                table: "CorrectAnswers");

            migrationBuilder.AlterColumn<int>(
                name: "QuestionId",
                table: "CorrectAnswers",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "CorrectAnswers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddForeignKey(
                name: "FK_CorrectAnswers_Questions_QuestionId",
                table: "CorrectAnswers",
                column: "QuestionId",
                principalTable: "Questions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CorrectAnswers_Questions_QuestionId",
                table: "CorrectAnswers");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "CorrectAnswers");

            migrationBuilder.AlterColumn<int>(
                name: "QuestionId",
                table: "CorrectAnswers",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddForeignKey(
                name: "FK_CorrectAnswers_Questions_QuestionId",
                table: "CorrectAnswers",
                column: "QuestionId",
                principalTable: "Questions",
                principalColumn: "Id");
        }
    }
}
