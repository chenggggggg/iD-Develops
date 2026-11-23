using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMultipleOpenAnswerInputs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OpenAnswerFields",
                table: "Questions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.Sql("UPDATE \"Questions\" SET \"OpenAnswerFields\" = '[]'::jsonb WHERE \"QuestionType\" = 'Open' AND \"OpenAnswerFields\" IS NULL");

            migrationBuilder.AddColumn<string>(
                name: "OpenAnswerFields",
                table: "ExamVersionQuestions",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OpenAnswerFields",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "OpenAnswerFields",
                table: "ExamVersionQuestions");
        }
    }
}
