using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class AddCatalogProductSecureInvites : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireAccessToken",
                table: "CatalogProducts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CatalogProductInvites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CatalogProductId = table.Column<int>(type: "int", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AllowedEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MaxUses = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogProductInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogProductInvites_CatalogProducts_CatalogProductId",
                        column: x => x.CatalogProductId,
                        principalTable: "CatalogProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogProductInviteUses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CatalogProductInviteId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CustomerEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StripeSessionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogProductInviteUses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogProductInviteUses_CatalogProductInvites_CatalogProductInviteId",
                        column: x => x.CatalogProductInviteId,
                        principalTable: "CatalogProductInvites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProductInvites_CatalogProductId",
                table: "CatalogProductInvites",
                column: "CatalogProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProductInvites_Token",
                table: "CatalogProductInvites",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogProductInviteUses_CatalogProductInviteId",
                table: "CatalogProductInviteUses",
                column: "CatalogProductInviteId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogProductInviteUses");

            migrationBuilder.DropTable(
                name: "CatalogProductInvites");

            migrationBuilder.DropColumn(
                name: "RequireAccessToken",
                table: "CatalogProducts");
        }
    }
}
