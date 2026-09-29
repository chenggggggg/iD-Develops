using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExamGradeBands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExamGradeBands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamId = table.Column<int>(type: "int", nullable: false),
                    MinimumScore = table.Column<double>(type: "float", nullable: false),
                    LabelPrimary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LabelSecondary = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamGradeBands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamGradeBands_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamVersionGradeBands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExamVersionId = table.Column<int>(type: "int", nullable: false),
                    MinimumScore = table.Column<double>(type: "float", nullable: false),
                    LabelPrimary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LabelSecondary = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamVersionGradeBands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamVersionGradeBands_ExamVersions_ExamVersionId",
                        column: x => x.ExamVersionId,
                        principalTable: "ExamVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamGradeBands_ExamId",
                table: "ExamGradeBands",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamVersionGradeBands_ExamVersionId",
                table: "ExamVersionGradeBands",
                column: "ExamVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExamGradeBands");

            migrationBuilder.DropTable(
                name: "ExamVersionGradeBands");
        }
    }
}
