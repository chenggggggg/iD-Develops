using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddZoomRegistrationAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AttendanceReconciledAtUtc",
                table: "ScheduledEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttendanceReconciliationError",
                table: "ScheduledEvents",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ZoomEndedAtUtc",
                table: "ScheduledEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoomMeetingUuid",
                table: "ScheduledEvents",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AttendanceManuallyOverridden",
                table: "EventBookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "AttendanceReconciledAtUtc",
                table: "EventBookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttendanceResolutionSource",
                table: "EventBookings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreditResolution",
                table: "EventBookings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreditResolvedAtUtc",
                table: "EventBookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstJoinedAtUtc",
                table: "EventBookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLeftAtUtc",
                table: "EventBookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedZoomJoinUrl",
                table: "EventBookings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ZoomAttendanceSeconds",
                table: "EventBookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ZoomRegistrantId",
                table: "EventBookings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoomRegistrationError",
                table: "EventBookings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZoomRegistrationStatus",
                table: "EventBookings",
                type: "text",
                nullable: false,
                defaultValue: "NotRequired");

            migrationBuilder.AddColumn<DateTime>(
                name: "ZoomRegistrationSyncedAtUtc",
                table: "EventBookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsConsumed",
                table: "EventBookingCreditAllocations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ZoomAttendanceSegments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventBookingId = table.Column<int>(type: "integer", nullable: false),
                    ParticipantSessionId = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ZoomMeetingUuid = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    JoinedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeftAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZoomAttendanceSegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ZoomAttendanceSegments_EventBookings_EventBookingId",
                        column: x => x.EventBookingId,
                        principalTable: "EventBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventBookings_ZoomRegistrantId",
                table: "EventBookings",
                column: "ZoomRegistrantId");

            migrationBuilder.CreateIndex(
                name: "IX_ZoomAttendanceSegments_EventBookingId_ParticipantSessionId_~",
                table: "ZoomAttendanceSegments",
                columns: new[] { "EventBookingId", "ParticipantSessionId", "JoinedAtUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZoomAttendanceSegments");

            migrationBuilder.DropIndex(
                name: "IX_EventBookings_ZoomRegistrantId",
                table: "EventBookings");

            migrationBuilder.DropColumn(
                name: "AttendanceReconciledAtUtc",
                table: "ScheduledEvents");

            migrationBuilder.DropColumn(
                name: "AttendanceReconciliationError",
                table: "ScheduledEvents");

            migrationBuilder.DropColumn(
                name: "ZoomEndedAtUtc",
                table: "ScheduledEvents");

            migrationBuilder.DropColumn(
                name: "ZoomMeetingUuid",
                table: "ScheduledEvents");

            migrationBuilder.DropColumn(
                name: "AttendanceManuallyOverridden",
                table: "EventBookings");

            migrationBuilder.DropColumn(
                name: "AttendanceReconciledAtUtc",
                table: "EventBookings");

            migrationBuilder.DropColumn(
                name: "AttendanceResolutionSource",
                table: "EventBookings");

            migrationBuilder.DropColumn(
                name: "CreditResolution",
                table: "EventBookings");

            migrationBuilder.DropColumn(
                name: "CreditResolvedAtUtc",
                table: "EventBookings");

            migrationBuilder.DropColumn(
                name: "FirstJoinedAtUtc",
                table: "EventBookings");

            migrationBuilder.DropColumn(
                name: "LastLeftAtUtc",
                table: "EventBookings");

            migrationBuilder.DropColumn(
                name: "ProtectedZoomJoinUrl",
                table: "EventBookings");

            migrationBuilder.DropColumn(
                name: "ZoomAttendanceSeconds",
                table: "EventBookings");

            migrationBuilder.DropColumn(
                name: "ZoomRegistrantId",
                table: "EventBookings");

            migrationBuilder.DropColumn(
                name: "ZoomRegistrationError",
                table: "EventBookings");

            migrationBuilder.DropColumn(
                name: "ZoomRegistrationStatus",
                table: "EventBookings");

            migrationBuilder.DropColumn(
                name: "ZoomRegistrationSyncedAtUtc",
                table: "EventBookings");

            migrationBuilder.DropColumn(
                name: "IsConsumed",
                table: "EventBookingCreditAllocations");
        }
    }
}
