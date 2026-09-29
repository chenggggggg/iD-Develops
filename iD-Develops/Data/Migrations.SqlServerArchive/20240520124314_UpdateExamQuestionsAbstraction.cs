using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class UpdateExamQuestionsAbstraction : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "FK_TrueOrFalseAnswers_Questions_QuestionId",
                table: "TrueOrFalseAnswers");

            migrationBuilder.DropIndex(
                name: "IX_TrueOrFalseAnswers_QuestionId",
                table: "TrueOrFalseAnswers");

            migrationBuilder.DropIndex(
                name: "IX_OpenAnswers_QuestionId",
                table: "OpenAnswers");

            migrationBuilder.DropIndex(
                name: "IX_MultipleChoiceAnswers_QuestionId",
                table: "MultipleChoiceAnswers");

            migrationBuilder.RenameColumn(
                name: "Type",
                table: "Questions",
                newName: "QuestionType");

            migrationBuilder.AddColumn<Guid>(
                name: "ParticipantId",
                table: "ParticipantAnswers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_TrueOrFalseAnswers_QuestionId",
                table: "TrueOrFalseAnswers",
                column: "QuestionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpenAnswers_QuestionId",
                table: "OpenAnswers",
                column: "QuestionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MultipleChoiceAnswers_QuestionId",
                table: "MultipleChoiceAnswers",
                column: "QuestionId",
                unique: true);

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
                name: "FK_TrueOrFalseAnswers_Questions_QuestionId",
                table: "TrueOrFalseAnswers",
                column: "QuestionId",
                principalTable: "Questions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "FK_TrueOrFalseAnswers_Questions_QuestionId",
                table: "TrueOrFalseAnswers");

            migrationBuilder.DropIndex(
                name: "IX_TrueOrFalseAnswers_QuestionId",
                table: "TrueOrFalseAnswers");

            migrationBuilder.DropIndex(
                name: "IX_OpenAnswers_QuestionId",
                table: "OpenAnswers");

            migrationBuilder.DropIndex(
                name: "IX_MultipleChoiceAnswers_QuestionId",
                table: "MultipleChoiceAnswers");

            migrationBuilder.DropColumn(
                name: "ParticipantId",
                table: "ParticipantAnswers");

            migrationBuilder.RenameColumn(
                name: "QuestionType",
                table: "Questions",
                newName: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_TrueOrFalseAnswers_QuestionId",
                table: "TrueOrFalseAnswers",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenAnswers_QuestionId",
                table: "OpenAnswers",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_MultipleChoiceAnswers_QuestionId",
                table: "MultipleChoiceAnswers",
                column: "QuestionId");

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
                name: "FK_TrueOrFalseAnswers_Questions_QuestionId",
                table: "TrueOrFalseAnswers",
                column: "QuestionId",
                principalTable: "Questions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
