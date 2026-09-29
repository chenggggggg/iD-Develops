using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseClassProgressAndBookingVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsVisibleForStudentBooking",
                table: "ScheduleRosterRules",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVisibleForStudentBooking",
                table: "ScheduledEvents",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "BookingEligibility",
                table: "CourseClasses",
                type: "text",
                nullable: false,
                defaultValue: "WhenClassUnlocks");

            migrationBuilder.AddColumn<bool>(
                name: "IsRequiredForCompletion",
                table: "CourseClasses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsVisibleForStudentBooking",
                table: "CourseClasses",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsVisibleForStudentBooking",
                table: "ScheduleRosterRules");

            migrationBuilder.DropColumn(
                name: "IsVisibleForStudentBooking",
                table: "ScheduledEvents");

            migrationBuilder.DropColumn(
                name: "BookingEligibility",
                table: "CourseClasses");

            migrationBuilder.DropColumn(
                name: "IsRequiredForCompletion",
                table: "CourseClasses");

            migrationBuilder.DropColumn(
                name: "IsVisibleForStudentBooking",
                table: "CourseClasses");
        }
    }
}
