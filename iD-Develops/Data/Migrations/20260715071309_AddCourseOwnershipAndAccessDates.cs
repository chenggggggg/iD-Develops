using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseOwnershipAndAccessDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GrantedAtUtc",
                table: "UserCourses",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Courses",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Courses_CreatedByUserId",
                table: "Courses",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Courses_AspNetUsers_CreatedByUserId",
                table: "Courses",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Courses_AspNetUsers_CreatedByUserId",
                table: "Courses");

            migrationBuilder.DropIndex(
                name: "IX_Courses_CreatedByUserId",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "GrantedAtUtc",
                table: "UserCourses");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Courses");
        }
    }
}
