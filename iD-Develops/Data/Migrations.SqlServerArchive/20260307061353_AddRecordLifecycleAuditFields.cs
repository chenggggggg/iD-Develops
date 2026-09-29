using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class AddRecordLifecycleAuditFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastActivityUtc",
                table: "Records",
                type: "datetime2(0)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StatusChangedAtUtc",
                table: "Records",
                type: "datetime2(0)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusReason",
                table: "Records",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "None");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastActivityUtc",
                table: "Records");

            migrationBuilder.DropColumn(
                name: "StatusChangedAtUtc",
                table: "Records");

            migrationBuilder.DropColumn(
                name: "StatusReason",
                table: "Records");
        }
    }
}

