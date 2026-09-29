using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class UpdateRelationshipsAndForeignKeys : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exams_LanguageLevels_LanguageLevelId",
                table: "Exams");

            migrationBuilder.DropForeignKey(
                name: "FK_MultipleChoiceAnswers_Questions_QuestionId",
                table: "MultipleChoiceAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_OpenAnswers_Questions_QuestionId",
                table: "OpenAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_ParticipantAnswers_Questions_QuestionId",
                table: "ParticipantAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_Questions_Exams_ExamId",
                table: "Questions");

            migrationBuilder.DropForeignKey(
                name: "FK_Questions_LanguageLevels_LanguageLevelId",
                table: "Questions");

            migrationBuilder.DropForeignKey(
                name: "FK_Records_Exams_ExamId",
                table: "Records");

            migrationBuilder.DropForeignKey(
                name: "FK_TrueOrFalseAnswers_Questions_QuestionId",
                table: "TrueOrFalseAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_UserExam_AspNetUsers_UserId",
                table: "UserExam");

            migrationBuilder.DropForeignKey(
                name: "FK_UserExam_Exams_ExamId",
                table: "UserExam");

            migrationBuilder.DropIndex(
                name: "IX_Records_ExamId",
                table: "Records");

            migrationBuilder.DropIndex(
                name: "IX_Questions_LanguageLevelId",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_ParticipantAnswers_QuestionId",
                table: "ParticipantAnswers");

            migrationBuilder.DropIndex(
                name: "IX_Exams_LanguageLevelId",
                table: "Exams");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "TrueOrFalseAnswers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Records",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "ExamId",
                table: "Questions",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Questions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ParticipantAnswers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "OpenAnswers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "MultipleChoiceAnswers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "LanguageLevels",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Exams",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ExamQuestions",
                columns: table => new
                {
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    QuestionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamQuestions", x => new { x.ExamId, x.QuestionId });
                    table.ForeignKey(
                        name: "FK_ExamQuestions_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamQuestions_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Records_ExamId",
                table: "Records",
                column: "ExamId",
                unique: false);

            migrationBuilder.CreateIndex(
                name: "IX_Questions_LanguageLevelId",
                table: "Questions",
                column: "LanguageLevelId",
                unique: false);

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantAnswers_QuestionId",
                table: "ParticipantAnswers",
                column: "QuestionId",
                unique: false);

            migrationBuilder.CreateIndex(
                name: "IX_Exams_LanguageLevelId",
                table: "Exams",
                column: "LanguageLevelId",
                unique: false);

            migrationBuilder.CreateIndex(
                name: "IX_ExamQuestions_QuestionId",
                table: "ExamQuestions",
                column: "QuestionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Exams_LanguageLevels_LanguageLevelId",
                table: "Exams",
                column: "LanguageLevelId",
                principalTable: "LanguageLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MultipleChoiceAnswers_Questions_QuestionId",
                table: "MultipleChoiceAnswers",
                column: "QuestionId",
                principalTable: "Questions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OpenAnswers_Questions_QuestionId",
                table: "OpenAnswers",
                column: "QuestionId",
                principalTable: "Questions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ParticipantAnswers_Questions_QuestionId",
                table: "ParticipantAnswers",
                column: "QuestionId",
                principalTable: "Questions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Questions_Exams_ExamId",
                table: "Questions",
                column: "ExamId",
                principalTable: "Exams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Questions_LanguageLevels_LanguageLevelId",
                table: "Questions",
                column: "LanguageLevelId",
                principalTable: "LanguageLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Records_Exams_ExamId",
                table: "Records",
                column: "ExamId",
                principalTable: "Exams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrueOrFalseAnswers_Questions_QuestionId",
                table: "TrueOrFalseAnswers",
                column: "QuestionId",
                principalTable: "Questions",
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

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Exams_LanguageLevels_LanguageLevelId",
                table: "Exams");

            migrationBuilder.DropForeignKey(
                name: "FK_MultipleChoiceAnswers_Questions_QuestionId",
                table: "MultipleChoiceAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_OpenAnswers_Questions_QuestionId",
                table: "OpenAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_ParticipantAnswers_Questions_QuestionId",
                table: "ParticipantAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_Questions_Exams_ExamId",
                table: "Questions");

            migrationBuilder.DropForeignKey(
                name: "FK_Questions_LanguageLevels_LanguageLevelId",
                table: "Questions");

            migrationBuilder.DropForeignKey(
                name: "FK_Records_Exams_ExamId",
                table: "Records");

            migrationBuilder.DropForeignKey(
                name: "FK_TrueOrFalseAnswers_Questions_QuestionId",
                table: "TrueOrFalseAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_UserExam_AspNetUsers_UserId",
                table: "UserExam");

            migrationBuilder.DropForeignKey(
                name: "FK_UserExam_Exams_ExamId",
                table: "UserExam");

            migrationBuilder.DropTable(
                name: "ExamQuestions");

            migrationBuilder.DropIndex(
                name: "IX_Records_ExamId",
                table: "Records");

            migrationBuilder.DropIndex(
                name: "IX_Questions_LanguageLevelId",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_ParticipantAnswers_QuestionId",
                table: "ParticipantAnswers");

            migrationBuilder.DropIndex(
                name: "IX_Exams_LanguageLevelId",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "TrueOrFalseAnswers");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Records");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ParticipantAnswers");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "OpenAnswers");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "MultipleChoiceAnswers");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "LanguageLevels");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "AspNetUsers");

            migrationBuilder.AlterColumn<int>(
                name: "ExamId",
                table: "Questions",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateIndex(
                name: "IX_Records_ExamId",
                table: "Records",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_LanguageLevelId",
                table: "Questions",
                column: "LanguageLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantAnswers_QuestionId",
                table: "ParticipantAnswers",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_LanguageLevelId",
                table: "Exams",
                column: "LanguageLevelId");

            migrationBuilder.AddForeignKey(
                name: "FK_Exams_LanguageLevels_LanguageLevelId",
                table: "Exams",
                column: "LanguageLevelId",
                principalTable: "LanguageLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

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
                name: "FK_ParticipantAnswers_Questions_QuestionId",
                table: "ParticipantAnswers",
                column: "QuestionId",
                principalTable: "Questions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Questions_Exams_ExamId",
                table: "Questions",
                column: "ExamId",
                principalTable: "Exams",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Questions_LanguageLevels_LanguageLevelId",
                table: "Questions",
                column: "LanguageLevelId",
                principalTable: "LanguageLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Records_Exams_ExamId",
                table: "Records",
                column: "ExamId",
                principalTable: "Exams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TrueOrFalseAnswers_Questions_QuestionId",
                table: "TrueOrFalseAnswers",
                column: "QuestionId",
                principalTable: "Questions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserExam_AspNetUsers_UserId",
                table: "UserExam",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserExam_Exams_ExamId",
                table: "UserExam",
                column: "ExamId",
                principalTable: "Exams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
