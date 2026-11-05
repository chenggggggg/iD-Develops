using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptionalEventTypeCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "RequiredCreditTypeId",
                table: "AppointmentTypes",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "CreditConsumptionPolicyId",
                table: "AppointmentTypes",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "AppointmentTypes" WHERE "RequiredCreditTypeId" IS NULL OR "CreditConsumptionPolicyId" IS NULL)
                       OR EXISTS (SELECT 1 FROM "ScheduledEvents" WHERE "BookingAccess" = 'Free') THEN
                        RAISE EXCEPTION 'Cannot revert optional event credits while free event types or meetings exist. Review those records before downgrading; no records have been removed.';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "RequiredCreditTypeId",
                table: "AppointmentTypes",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "CreditConsumptionPolicyId",
                table: "AppointmentTypes",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
