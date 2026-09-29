using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class AddUnsubscribePropertiesForProspects : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBounced",
                table: "Prospects",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSuppressed",
                table: "Prospects",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsUnsubscribed",
                table: "Prospects",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "UnsubscribeToken",
                table: "Prospects",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBounced",
                table: "Prospects");

            migrationBuilder.DropColumn(
                name: "IsSuppressed",
                table: "Prospects");

            migrationBuilder.DropColumn(
                name: "IsUnsubscribed",
                table: "Prospects");

            migrationBuilder.DropColumn(
                name: "UnsubscribeToken",
                table: "Prospects");
        }
    }
}
