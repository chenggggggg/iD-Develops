using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncrementalExamSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangesJson",
                table: "ExamVersions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentVersionId",
                table: "ExamVersions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamVersions_ParentVersionId",
                table: "ExamVersions",
                column: "ParentVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExamVersions_ExamVersions_ParentVersionId",
                table: "ExamVersions",
                column: "ParentVersionId",
                principalTable: "ExamVersions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExamVersions_ExamVersions_ParentVersionId",
                table: "ExamVersions");

            migrationBuilder.DropIndex(
                name: "IX_ExamVersions_ParentVersionId",
                table: "ExamVersions");

            migrationBuilder.DropColumn(
                name: "ChangesJson",
                table: "ExamVersions");

            migrationBuilder.DropColumn(
                name: "ParentVersionId",
                table: "ExamVersions");
        }
    }
}
