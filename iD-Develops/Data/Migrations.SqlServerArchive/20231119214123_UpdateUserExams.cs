using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class UpdateUserExams : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationUserExam");

            migrationBuilder.AddColumn<int>(
                name: "LanguageLevelId",
                table: "Exams",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "UserExam",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ExamId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserExam", x => new { x.UserId, x.ExamId });
                    table.ForeignKey(
                        name: "FK_UserExam_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserExam_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Exams_LanguageLevelId",
                table: "Exams",
                column: "LanguageLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_UserExam_ExamId",
                table: "UserExam",
                column: "ExamId");

            migrationBuilder.AddForeignKey(
                name: "FK_Exams_LanguageLevels_LanguageLevelId",
                table: "Exams",
                column: "LanguageLevelId",
                principalTable: "LanguageLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exams_LanguageLevels_LanguageLevelId",
                table: "Exams");

            migrationBuilder.DropTable(
                name: "UserExam");

            migrationBuilder.DropIndex(
                name: "IX_Exams_LanguageLevelId",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "LanguageLevelId",
                table: "Exams");

            migrationBuilder.CreateTable(
                name: "ApplicationUserExam",
                columns: table => new
                {
                    ExamsId = table.Column<int>(type: "int", nullable: false),
                    UsersId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationUserExam", x => new { x.ExamsId, x.UsersId });
                    table.ForeignKey(
                        name: "FK_ApplicationUserExam_AspNetUsers_UsersId",
                        column: x => x.UsersId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApplicationUserExam_Exams_ExamsId",
                        column: x => x.ExamsId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUserExam_UsersId",
                table: "ApplicationUserExam",
                column: "UsersId");
        }
    }
}
