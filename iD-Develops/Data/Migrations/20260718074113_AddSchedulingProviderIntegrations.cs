using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class AddSchedulingProviderIntegrations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalSyncError",
                table: "ScheduledEvents",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExternalSyncedAtUtc",
                table: "ScheduledEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleCalendarEventId",
                table: "ScheduledEvents",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleCalendarId",
                table: "ScheduledEvents",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoomMeetingId",
                table: "ScheduledEvents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SchedulingProviderConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "text", nullable: false),
                    ProtectedRefreshToken = table.Column<string>(type: "text", nullable: true),
                    AccessTokenExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProviderAccountId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ProviderEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    GrantedScopes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CalendarId = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ConnectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSuccessfulSyncAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulingProviderConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchedulingProviderConnections_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingProviderConnections_UserId_Provider",
                table: "SchedulingProviderConnections",
                columns: new[] { "UserId", "Provider" },
                unique: true);

            migrationBuilder.Sql(
                """
                DELETE FROM "ScheduledEvents" AS scheduled
                WHERE scheduled."Source" = 'Roster'
                  AND scheduled."DeliveryType" = 'Zoom'
                  AND scheduled."StartAtUtc" > NOW()
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "EventBookings" AS booking
                      WHERE booking."ScheduledEventId" = scheduled."Id"
                        AND booking."Status" IN ('Confirmed', 'Attended'));
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SchedulingProviderConnections");
            migrationBuilder.DropColumn(name: "ExternalSyncError", table: "ScheduledEvents");
            migrationBuilder.DropColumn(name: "ExternalSyncedAtUtc", table: "ScheduledEvents");
            migrationBuilder.DropColumn(name: "GoogleCalendarEventId", table: "ScheduledEvents");
            migrationBuilder.DropColumn(name: "GoogleCalendarId", table: "ScheduledEvents");
            migrationBuilder.DropColumn(name: "ZoomMeetingId", table: "ScheduledEvents");
        }
    }
}
