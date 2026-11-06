using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class UnifiedAppointmentBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BookedByUserId",
                table: "EventBookings",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryType",
                table: "AppointmentTypes",
                type: "text",
                nullable: false,
                defaultValue: "Zoom");

            // Existing private-session types used Zoom. Future inserts supply the
            // delivery setting from the model rather than a database default.
            migrationBuilder.Sql("ALTER TABLE \"AppointmentTypes\" ALTER COLUMN \"DeliveryType\" DROP DEFAULT;");

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "AppointmentTypes",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MeetingUrl",
                table: "AppointmentTypes",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppointmentBookingRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    AttendeeUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    TeacherUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    AppointmentTypeId = table.Column<int>(type: "integer", nullable: false),
                    StartAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScheduledEventId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentBookingRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentBookingRequests_ScheduledEvents_ScheduledEventId",
                        column: x => x.ScheduledEventId,
                        principalTable: "ScheduledEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentBookingRequests_AttendeeUserId_CreatedAtUtc",
                table: "AppointmentBookingRequests",
                columns: new[] { "AttendeeUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentBookingRequests_ScheduledEventId",
                table: "AppointmentBookingRequests",
                column: "ScheduledEventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "AppointmentBookingRequests" WHERE "Status" IN ('Preparing', 'ReviewRequired')) THEN
                        RAISE EXCEPTION 'Cannot remove booking request tracking while meeting setup is unresolved. Review and resolve pending requests before downgrading.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "AppointmentBookingRequests");

            migrationBuilder.DropColumn(
                name: "BookedByUserId",
                table: "EventBookings");

            migrationBuilder.DropColumn(
                name: "DeliveryType",
                table: "AppointmentTypes");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "AppointmentTypes");

            migrationBuilder.DropColumn(
                name: "MeetingUrl",
                table: "AppointmentTypes");
        }
    }
}
