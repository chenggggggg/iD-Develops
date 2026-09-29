using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelAfterExamTakeAndEditorRefactors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExamVersionId",
                table: "Records",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExamVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DifficultyValue = table.Column<int>(type: "int", nullable: false),
                    TimeLimit = table.Column<int>(type: "int", nullable: true),
                    IntroductionPrimaryLanguage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IntroductionSecondaryLanguage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompletionTextPrimary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompletionTextSecondary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MaxScore = table.Column<double>(type: "float", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    PublicSlug = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CourseName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamVersions_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamVersionQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamVersionId = table.Column<int>(type: "int", nullable: false),
                    SourceQuestionId = table.Column<int>(type: "int", nullable: false),
                    QuestionType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    QuestionNumber = table.Column<int>(type: "int", nullable: false),
                    MessageBeforeQuestion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImageReference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AudioReference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Scenario = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Feedback = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FunFact = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnswerA = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnswerB = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnswerC = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnswerD = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamVersionQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamVersionQuestions_ExamVersions_ExamVersionId",
                        column: x => x.ExamVersionId,
                        principalTable: "ExamVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamVersionCorrectAnswers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamVersionQuestionId = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Score = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamVersionCorrectAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamVersionCorrectAnswers_ExamVersionQuestions_ExamVersionQuestionId",
                        column: x => x.ExamVersionQuestionId,
                        principalTable: "ExamVersionQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Records_ExamVersionId",
                table: "Records",
                column: "ExamVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamVersionCorrectAnswers_ExamVersionQuestionId",
                table: "ExamVersionCorrectAnswers",
                column: "ExamVersionQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamVersionQuestions_ExamVersionId",
                table: "ExamVersionQuestions",
                column: "ExamVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamVersions_ExamId_VersionNumber",
                table: "ExamVersions",
                columns: new[] { "ExamId", "VersionNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Records_ExamVersions_ExamVersionId",
                table: "Records",
                column: "ExamVersionId",
                principalTable: "ExamVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Records_ExamVersions_ExamVersionId",
                table: "Records");

            migrationBuilder.DropTable(
                name: "ExamVersionCorrectAnswers");

            migrationBuilder.DropTable(
                name: "ExamVersionQuestions");

            migrationBuilder.DropTable(
                name: "ExamVersions");

            migrationBuilder.DropIndex(
                name: "IX_Records_ExamVersionId",
                table: "Records");

            migrationBuilder.DropColumn(
                name: "ExamVersionId",
                table: "Records");
        }
    }
}
