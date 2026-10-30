using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class QueueSignupNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestKey",
                table: "ProductFormSubmissions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "CatalogCheckoutMails",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.CreateIndex(
                name: "IX_ProductFormSubmissions_RequestKey",
                table: "ProductFormSubmissions",
                column: "RequestKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductFormSubmissions_RequestKey",
                table: "ProductFormSubmissions");

            migrationBuilder.DropColumn(
                name: "RequestKey",
                table: "ProductFormSubmissions");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "CatalogCheckoutMails");
        }
    }
}
