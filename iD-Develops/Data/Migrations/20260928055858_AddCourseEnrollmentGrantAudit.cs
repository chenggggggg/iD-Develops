using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseEnrollmentGrantAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GrantedByUserId",
                table: "UserCourses",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserCourses_GrantedByUserId",
                table: "UserCourses",
                column: "GrantedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserCourses_AspNetUsers_GrantedByUserId",
                table: "UserCourses",
                column: "GrantedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserCourses_AspNetUsers_GrantedByUserId",
                table: "UserCourses");

            migrationBuilder.DropIndex(
                name: "IX_UserCourses_GrantedByUserId",
                table: "UserCourses");

            migrationBuilder.DropColumn(
                name: "GrantedByUserId",
                table: "UserCourses");
        }
    }
}
