using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class RemoveExamTagsFeature : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExamExamTags");

            migrationBuilder.DropTable(
                name: "ExamTags");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "IX_ExamExamTags_ExamTagId",
                table: "ExamExamTags",
                column: "ExamTagId");
        }
    }
}
