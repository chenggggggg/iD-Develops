using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceCourseUnlockDatesWithDelays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnlockAtUtc",
                table: "Lectures");

            migrationBuilder.DropColumn(
                name: "UnlockAtUtc",
                table: "CourseSections");

            migrationBuilder.DropColumn(
                name: "UnlockAtUtc",
                table: "CourseClasses");

            migrationBuilder.AddColumn<string>(
                name: "UnlockAfterUnit",
                table: "Lectures",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnlockAfterValue",
                table: "Lectures",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnlockAfterUnit",
                table: "CourseSections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnlockAfterValue",
                table: "CourseSections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnlockAfterUnit",
                table: "CourseClasses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnlockAfterValue",
                table: "CourseClasses",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnlockAfterUnit",
                table: "Lectures");

            migrationBuilder.DropColumn(
                name: "UnlockAfterValue",
                table: "Lectures");

            migrationBuilder.DropColumn(
                name: "UnlockAfterUnit",
                table: "CourseSections");

            migrationBuilder.DropColumn(
                name: "UnlockAfterValue",
                table: "CourseSections");

            migrationBuilder.DropColumn(
                name: "UnlockAfterUnit",
                table: "CourseClasses");

            migrationBuilder.DropColumn(
                name: "UnlockAfterValue",
                table: "CourseClasses");

            migrationBuilder.AddColumn<DateTime>(
                name: "UnlockAtUtc",
                table: "Lectures",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UnlockAtUtc",
                table: "CourseSections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UnlockAtUtc",
                table: "CourseClasses",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
