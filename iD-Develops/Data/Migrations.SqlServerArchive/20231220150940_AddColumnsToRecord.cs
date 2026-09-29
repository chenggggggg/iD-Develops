using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class AddColumnsToRecord : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EndDateTime",
                table: "Records",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "ExamStatus",
                table: "Records",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "StartDateTime",
                table: "Records",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "RecordId",
                table: "ParticipantAnswers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantAnswers_RecordId",
                table: "ParticipantAnswers",
                column: "RecordId");

            migrationBuilder.AddForeignKey(
                name: "FK_ParticipantAnswers_Records_RecordId",
                table: "ParticipantAnswers",
                column: "RecordId",
                principalTable: "Records",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ParticipantAnswers_Records_RecordId",
                table: "ParticipantAnswers");

            migrationBuilder.DropIndex(
                name: "IX_ParticipantAnswers_RecordId",
                table: "ParticipantAnswers");

            migrationBuilder.DropColumn(
                name: "EndDateTime",
                table: "Records");

            migrationBuilder.DropColumn(
                name: "ExamStatus",
                table: "Records");

            migrationBuilder.DropColumn(
                name: "StartDateTime",
                table: "Records");

            migrationBuilder.DropColumn(
                name: "RecordId",
                table: "ParticipantAnswers");
        }
    }
}
