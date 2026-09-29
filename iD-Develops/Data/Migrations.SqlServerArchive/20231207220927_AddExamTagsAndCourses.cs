using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class AddExamTagsAndCourses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exams_Difficulty_DifficultyId",
                table: "Exams");

            migrationBuilder.DropForeignKey(
                name: "FK_Questions_Difficulty_DifficultyId",
                table: "Questions");

            migrationBuilder.DropForeignKey(
                name: "FK_UserExam_AspNetUsers_UserId",
                table: "UserExam");

            migrationBuilder.DropForeignKey(
                name: "FK_UserExam_Exams_ExamId",
                table: "UserExam");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserExam",
                table: "UserExam");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Difficulty",
                table: "Difficulty");

            migrationBuilder.RenameTable(
                name: "UserExam",
                newName: "UserExams");

            migrationBuilder.RenameTable(
                name: "Difficulty",
                newName: "Difficulties");

            migrationBuilder.RenameIndex(
                name: "IX_UserExam_ExamId",
                table: "UserExams",
                newName: "IX_UserExams_ExamId");

            migrationBuilder.AlterColumn<string>(
                name: "IntroductionSecondaryLanguage",
                table: "Exams",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "CompletionTextPrimary",
                table: "Exams",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CompletionTextSecondary",
                table: "Exams",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserExams",
                table: "UserExams",
                columns: new[] { "UserId", "ExamId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Difficulties",
                table: "Difficulties",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "Courses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExamTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamTags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExamCourses",
                columns: table => new
                {
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    CourseId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamCourses", x => new { x.ExamId, x.CourseId });
                    table.ForeignKey(
                        name: "FK_ExamCourses_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamCourses_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserCourses",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CourseId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCourses", x => new { x.UserId, x.CourseId });
                    table.ForeignKey(
                        name: "FK_UserCourses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserCourses_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamExamTags",
                columns: table => new
                {
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    ExamTagId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamExamTags", x => new { x.ExamId, x.ExamTagId });
                    table.ForeignKey(
                        name: "FK_ExamExamTags_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamExamTags_ExamTags_ExamTagId",
                        column: x => x.ExamTagId,
                        principalTable: "ExamTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamCourses_CourseId",
                table: "ExamCourses",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamExamTags_ExamTagId",
                table: "ExamExamTags",
                column: "ExamTagId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCourses_CourseId",
                table: "UserCourses",
                column: "CourseId");

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

            migrationBuilder.AddForeignKey(
                name: "FK_UserExams_AspNetUsers_UserId",
                table: "UserExams",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserExams_Exams_ExamId",
                table: "UserExams",
                column: "ExamId",
                principalTable: "Exams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exams_Difficulties_DifficultyId",
                table: "Exams");

            migrationBuilder.DropForeignKey(
                name: "FK_Questions_Difficulties_DifficultyId",
                table: "Questions");

            migrationBuilder.DropForeignKey(
                name: "FK_UserExams_AspNetUsers_UserId",
                table: "UserExams");

            migrationBuilder.DropForeignKey(
                name: "FK_UserExams_Exams_ExamId",
                table: "UserExams");

            migrationBuilder.DropTable(
                name: "ExamCourses");

            migrationBuilder.DropTable(
                name: "ExamExamTags");

            migrationBuilder.DropTable(
                name: "UserCourses");

            migrationBuilder.DropTable(
                name: "ExamTags");

            migrationBuilder.DropTable(
                name: "Courses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserExams",
                table: "UserExams");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Difficulties",
                table: "Difficulties");

            migrationBuilder.DropColumn(
                name: "CompletionTextPrimary",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "CompletionTextSecondary",
                table: "Exams");

            migrationBuilder.RenameTable(
                name: "UserExams",
                newName: "UserExam");

            migrationBuilder.RenameTable(
                name: "Difficulties",
                newName: "Difficulty");

            migrationBuilder.RenameIndex(
                name: "IX_UserExams_ExamId",
                table: "UserExam",
                newName: "IX_UserExam_ExamId");

            migrationBuilder.AlterColumn<string>(
                name: "IntroductionSecondaryLanguage",
                table: "Exams",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserExam",
                table: "UserExam",
                columns: new[] { "UserId", "ExamId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Difficulty",
                table: "Difficulty",
                column: "Id");

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

            migrationBuilder.AddForeignKey(
                name: "FK_UserExam_AspNetUsers_UserId",
                table: "UserExam",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserExam_Exams_ExamId",
                table: "UserExam",
                column: "ExamId",
                principalTable: "Exams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
