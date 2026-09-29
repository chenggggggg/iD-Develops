using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixRecordExamRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Records_ExamId",
                table: "Records");

            migrationBuilder.CreateIndex(
                name: "IX_Records_ExamId",
                table: "Records",
                column: "ExamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Records_ExamId",
                table: "Records");

            migrationBuilder.CreateIndex(
                name: "IX_Records_ExamId",
                table: "Records",
                column: "ExamId",
                unique: true);
        }
    }
}
