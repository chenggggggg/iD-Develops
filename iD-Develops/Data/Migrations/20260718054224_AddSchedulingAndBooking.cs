using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingAndBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRecommended",
                table: "CourseClasses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RecommendationWindowUnit",
                table: "CourseClasses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecommendationWindowValue",
                table: "CourseClasses",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecommendedAfterUnit",
                table: "CourseClasses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecommendedAfterValue",
                table: "CourseClasses",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ScheduleRosterRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeacherUserId = table.Column<string>(type: "text", nullable: false),
                    CourseClassId = table.Column<int>(type: "integer", nullable: false),
                    DayOfWeek = table.Column<string>(type: "text", nullable: false),
                    LocalStartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActiveFromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ActiveUntilDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    DeliveryType = table.Column<string>(type: "text", nullable: false),
                    MeetingUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    BookingOpenDaysBefore = table.Column<int>(type: "integer", nullable: false),
                    BookingCloseHoursBefore = table.Column<int>(type: "integer", nullable: false),
                    GenerateWeeksAhead = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleRosterRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleRosterRules_AspNetUsers_TeacherUserId",
                        column: x => x.TeacherUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduleRosterRules_CourseClasses_CourseClassId",
                        column: x => x.CourseClassId,
                        principalTable: "CourseClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserCreditLots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    CreditTypeId = table.Column<int>(type: "integer", nullable: false),
                    GrantedQuantity = table.Column<int>(type: "integer", nullable: false),
                    RemainingQuantity = table.Column<int>(type: "integer", nullable: false),
                    CourseId = table.Column<int>(type: "integer", nullable: true),
                    CourseClassId = table.Column<int>(type: "integer", nullable: true),
                    CatalogProductId = table.Column<int>(type: "integer", nullable: true),
                    CatalogProductCreditGrantId = table.Column<int>(type: "integer", nullable: true),
                    ExternalReference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    GrantedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCreditLots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserCreditLots_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserCreditLots_CatalogProductCreditGrants_CatalogProductCre~",
                        column: x => x.CatalogProductCreditGrantId,
                        principalTable: "CatalogProductCreditGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserCreditLots_CatalogProducts_CatalogProductId",
                        column: x => x.CatalogProductId,
                        principalTable: "CatalogProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserCreditLots_CourseClasses_CourseClassId",
                        column: x => x.CourseClassId,
                        principalTable: "CourseClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserCreditLots_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserCreditLots_CreditTypes_CreditTypeId",
                        column: x => x.CreditTypeId,
                        principalTable: "CreditTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScheduleRosterRuleId = table.Column<int>(type: "integer", nullable: true),
                    CourseClassId = table.Column<int>(type: "integer", nullable: true),
                    CourseId = table.Column<int>(type: "integer", nullable: true),
                    TeacherUserId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CourseNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClassNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TeacherNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    DeliveryType = table.Column<string>(type: "text", nullable: false),
                    MeetingUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    BookingOpensAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BookingClosesAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BookingAccess = table.Column<string>(type: "text", nullable: false),
                    RequiredCreditTypeId = table.Column<int>(type: "integer", nullable: true),
                    CreditCost = table.Column<int>(type: "integer", nullable: false),
                    CreditConsumptionPolicyId = table.Column<int>(type: "integer", nullable: true),
                    IsDetachedOverride = table.Column<bool>(type: "boolean", nullable: false),
                    CancellationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_AspNetUsers_TeacherUserId",
                        column: x => x.TeacherUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_CourseClasses_CourseClassId",
                        column: x => x.CourseClassId,
                        principalTable: "CourseClasses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_CreditConsumptionPolicies_CreditConsumption~",
                        column: x => x.CreditConsumptionPolicyId,
                        principalTable: "CreditConsumptionPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_CreditTypes_RequiredCreditTypeId",
                        column: x => x.RequiredCreditTypeId,
                        principalTable: "CreditTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledEvents_ScheduleRosterRules_ScheduleRosterRuleId",
                        column: x => x.ScheduleRosterRuleId,
                        principalTable: "ScheduleRosterRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO "ScheduledEvents" (
                    "CourseClassId", "CourseId", "TeacherUserId", "Title",
                    "CourseNameSnapshot", "ClassNameSnapshot", "TeacherNameSnapshot",
                    "StartAtUtc", "EndAtUtc", "TimeZoneId", "Capacity", "Status",
                    "Source", "DeliveryType", "MeetingUrl", "BookingOpensAtUtc",
                    "BookingClosesAtUtc", "BookingAccess", "RequiredCreditTypeId",
                    "CreditCost", "CreditConsumptionPolicyId", "IsDetachedOverride",
                    "CreatedAtUtc", "UpdatedAtUtc")
                SELECT
                    cc."Id",
                    cs."CourseId",
                    c."CreatedByUserId",
                    cc."Title",
                    LEFT(c."Name", 200),
                    LEFT(cc."Title", 200),
                    LEFT(COALESCE(
                        NULLIF(TRIM(CONCAT_WS(' ', u."FirstName", u."LastName")), ''),
                        u."UserName",
                        u."Email",
                        'Teacher'), 200),
                    cc."MeetingAtUtc",
                    cc."MeetingAtUtc" + (GREATEST(cc."DurationMinutes", 5) * INTERVAL '1 minute'),
                    'UTC',
                    GREATEST(cc."Capacity", 1),
                    'Scheduled',
                    'Manual',
                    CASE WHEN cc."MeetingLink" IS NULL THEN 'OtherOnline' ELSE 'Zoom' END,
                    cc."MeetingLink",
                    cc."MeetingAtUtc" - INTERVAL '30 days',
                    cc."MeetingAtUtc" - INTERVAL '1 hour',
                    cc."BookingAccess",
                    cc."RequiredCreditTypeId",
                    GREATEST(cc."CreditCost", 1),
                    cc."CreditConsumptionPolicyId",
                    TRUE,
                    NOW(),
                    NOW()
                FROM "CourseClasses" cc
                INNER JOIN "CourseSections" cs ON cs."Id" = cc."CourseSectionId"
                INNER JOIN "Courses" c ON c."Id" = cs."CourseId"
                INNER JOIN "AspNetUsers" u ON u."Id" = c."CreatedByUserId"
                WHERE cc."MeetingAtUtc" IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "EventBookings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScheduledEventId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    BookedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventBookings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventBookings_ScheduledEvents_ScheduledEventId",
                        column: x => x.ScheduledEventId,
                        principalTable: "ScheduledEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventBookingCreditAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventBookingId = table.Column<int>(type: "integer", nullable: false),
                    UserCreditLotId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    IsReturned = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventBookingCreditAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventBookingCreditAllocations_EventBookings_EventBookingId",
                        column: x => x.EventBookingId,
                        principalTable: "EventBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventBookingCreditAllocations_UserCreditLots_UserCreditLotId",
                        column: x => x.UserCreditLotId,
                        principalTable: "UserCreditLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserCreditTransactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    CreditTypeId = table.Column<int>(type: "integer", nullable: false),
                    UserCreditLotId = table.Column<int>(type: "integer", nullable: false),
                    EventBookingId = table.Column<int>(type: "integer", nullable: true),
                    TransactionType = table.Column<string>(type: "text", nullable: false),
                    QuantityDelta = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCreditTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserCreditTransactions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserCreditTransactions_CreditTypes_CreditTypeId",
                        column: x => x.CreditTypeId,
                        principalTable: "CreditTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserCreditTransactions_EventBookings_EventBookingId",
                        column: x => x.EventBookingId,
                        principalTable: "EventBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserCreditTransactions_UserCreditLots_UserCreditLotId",
                        column: x => x.UserCreditLotId,
                        principalTable: "UserCreditLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventBookingCreditAllocations_EventBookingId",
                table: "EventBookingCreditAllocations",
                column: "EventBookingId");

            migrationBuilder.CreateIndex(
                name: "IX_EventBookingCreditAllocations_UserCreditLotId",
                table: "EventBookingCreditAllocations",
                column: "UserCreditLotId");

            migrationBuilder.CreateIndex(
                name: "IX_EventBookings_ScheduledEventId_UserId",
                table: "EventBookings",
                columns: new[] { "ScheduledEventId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventBookings_UserId",
                table: "EventBookings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_CourseClassId",
                table: "ScheduledEvents",
                column: "CourseClassId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_CourseId",
                table: "ScheduledEvents",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_CreditConsumptionPolicyId",
                table: "ScheduledEvents",
                column: "CreditConsumptionPolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_RequiredCreditTypeId",
                table: "ScheduledEvents",
                column: "RequiredCreditTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_ScheduleRosterRuleId_StartAtUtc",
                table: "ScheduledEvents",
                columns: new[] { "ScheduleRosterRuleId", "StartAtUtc" },
                unique: true,
                filter: "\"ScheduleRosterRuleId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_StartAtUtc_EndAtUtc",
                table: "ScheduledEvents",
                columns: new[] { "StartAtUtc", "EndAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_TeacherUserId",
                table: "ScheduledEvents",
                column: "TeacherUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleRosterRules_CourseClassId",
                table: "ScheduleRosterRules",
                column: "CourseClassId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleRosterRules_TeacherUserId_CourseClassId_DayOfWeek_L~",
                table: "ScheduleRosterRules",
                columns: new[] { "TeacherUserId", "CourseClassId", "DayOfWeek", "LocalStartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_UserCreditLots_CatalogProductCreditGrantId",
                table: "UserCreditLots",
                column: "CatalogProductCreditGrantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCreditLots_CatalogProductId",
                table: "UserCreditLots",
                column: "CatalogProductId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCreditLots_CourseClassId",
                table: "UserCreditLots",
                column: "CourseClassId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCreditLots_CourseId",
                table: "UserCreditLots",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCreditLots_CreditTypeId",
                table: "UserCreditLots",
                column: "CreditTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCreditLots_ExternalReference_CatalogProductCreditGrantId",
                table: "UserCreditLots",
                columns: new[] { "ExternalReference", "CatalogProductCreditGrantId" },
                unique: true,
                filter: "\"ExternalReference\" IS NOT NULL AND \"CatalogProductCreditGrantId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserCreditLots_UserId",
                table: "UserCreditLots",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCreditTransactions_CreditTypeId",
                table: "UserCreditTransactions",
                column: "CreditTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCreditTransactions_EventBookingId",
                table: "UserCreditTransactions",
                column: "EventBookingId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCreditTransactions_UserCreditLotId",
                table: "UserCreditTransactions",
                column: "UserCreditLotId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCreditTransactions_UserId",
                table: "UserCreditTransactions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventBookingCreditAllocations");

            migrationBuilder.DropTable(
                name: "UserCreditTransactions");

            migrationBuilder.DropTable(
                name: "EventBookings");

            migrationBuilder.DropTable(
                name: "UserCreditLots");

            migrationBuilder.DropTable(
                name: "ScheduledEvents");

            migrationBuilder.DropTable(
                name: "ScheduleRosterRules");

            migrationBuilder.DropColumn(
                name: "IsRecommended",
                table: "CourseClasses");

            migrationBuilder.DropColumn(
                name: "RecommendationWindowUnit",
                table: "CourseClasses");

            migrationBuilder.DropColumn(
                name: "RecommendationWindowValue",
                table: "CourseClasses");

            migrationBuilder.DropColumn(
                name: "RecommendedAfterUnit",
                table: "CourseClasses");

            migrationBuilder.DropColumn(
                name: "RecommendedAfterValue",
                table: "CourseClasses");
        }
    }
}
