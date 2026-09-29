using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class UpdateRelationships : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Questions_DifficultyId",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Exams_DifficultyId",
                table: "Exams");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_DifficultyId",
                table: "Questions",
                column: "DifficultyId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_DifficultyId",
                table: "Exams",
                column: "DifficultyId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Questions_DifficultyId",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Exams_DifficultyId",
                table: "Exams");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_DifficultyId",
                table: "Questions",
                column: "DifficultyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Exams_DifficultyId",
                table: "Exams",
                column: "DifficultyId",
                unique: true);
        }
    }
}
