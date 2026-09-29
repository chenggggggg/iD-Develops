using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class ChangeLanguageLevelsTableName : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exams_LanguageLevels_LanguageLevelId",
                table: "Exams");

            migrationBuilder.DropForeignKey(
                name: "FK_Questions_LanguageLevels_LanguageLevelId",
                table: "Questions");

            migrationBuilder.DropTable(
                name: "LanguageLevels");

            migrationBuilder.RenameColumn(
                name: "LanguageLevelId",
                table: "Questions",
                newName: "DifficultyId");

            migrationBuilder.RenameIndex(
                name: "IX_Questions_LanguageLevelId",
                table: "Questions",
                newName: "IX_Questions_DifficultyId");

            migrationBuilder.RenameColumn(
                name: "LanguageLevelId",
                table: "Exams",
                newName: "DifficultyId");

            migrationBuilder.RenameIndex(
                name: "IX_Exams_LanguageLevelId",
                table: "Exams",
                newName: "IX_Exams_DifficultyId");

            migrationBuilder.CreateTable(
                name: "Difficulty",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Level = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Difficulty", x => x.Id);
                });

            migrationBuilder.AddForeignKey(
                name: "FK_Exams_Difficulty_DifficultyId",
                table: "Exams",
                column: "DifficultyId",
                principalTable: "Difficulty",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Questions_Difficulty_DifficultyId",
                table: "Questions",
                column: "DifficultyId",
                principalTable: "Difficulty",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exams_Difficulty_DifficultyId",
                table: "Exams");

            migrationBuilder.DropForeignKey(
                name: "FK_Questions_Difficulty_DifficultyId",
                table: "Questions");

            migrationBuilder.DropTable(
                name: "Difficulty");

            migrationBuilder.RenameColumn(
                name: "DifficultyId",
                table: "Questions",
                newName: "LanguageLevelId");

            migrationBuilder.RenameIndex(
                name: "IX_Questions_DifficultyId",
                table: "Questions",
                newName: "IX_Questions_LanguageLevelId");

            migrationBuilder.RenameColumn(
                name: "DifficultyId",
                table: "Exams",
                newName: "LanguageLevelId");

            migrationBuilder.RenameIndex(
                name: "IX_Exams_DifficultyId",
                table: "Exams",
                newName: "IX_Exams_LanguageLevelId");

            migrationBuilder.CreateTable(
                name: "LanguageLevels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LanguageLevels", x => x.Id);
                });

            migrationBuilder.AddForeignKey(
                name: "FK_Exams_LanguageLevels_LanguageLevelId",
                table: "Exams",
                column: "LanguageLevelId",
                principalTable: "LanguageLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Questions_LanguageLevels_LanguageLevelId",
                table: "Questions",
                column: "LanguageLevelId",
                principalTable: "LanguageLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
