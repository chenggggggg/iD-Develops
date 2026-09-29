using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class RemovedDifficultyTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exams_Difficulties_DifficultyId",
                table: "Exams");

            migrationBuilder.DropForeignKey(
                name: "FK_Questions_Difficulties_DifficultyId",
                table: "Questions");

            migrationBuilder.DropTable(
                name: "Difficulties");

            migrationBuilder.DropIndex(
                name: "IX_Questions_DifficultyId",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Exams_DifficultyId",
                table: "Exams");

            migrationBuilder.RenameColumn(
                name: "DifficultyId",
                table: "Questions",
                newName: "Difficulty");

            migrationBuilder.RenameColumn(
                name: "DifficultyId",
                table: "Exams",
                newName: "Difficulty");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Difficulty",
                table: "Questions",
                newName: "DifficultyId");

            migrationBuilder.RenameColumn(
                name: "Difficulty",
                table: "Exams",
                newName: "DifficultyId");

            migrationBuilder.CreateTable(
                name: "Difficulties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Difficulties", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_DifficultyId",
                table: "Questions",
                column: "DifficultyId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_DifficultyId",
                table: "Exams",
                column: "DifficultyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Exams_Difficulties_DifficultyId",
                table: "Exams",
                column: "DifficultyId",
                principalTable: "Difficulties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Questions_Difficulties_DifficultyId",
                table: "Questions",
                column: "DifficultyId",
                principalTable: "Difficulties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
