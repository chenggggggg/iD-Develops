using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261019080000_CompleteExpiredExamAttempts")]
    public class CompleteExpiredExamAttempts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve answers, scores, transition timestamps, and historical audit events.
            migrationBuilder.Sql(
                """
                UPDATE "Records"
                SET "ExamStatus" = 'Completed', "StatusReason" = 'DeadlinePassed'
                WHERE "ExamStatus" = 'Overdue';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Completed attempts are valid in the previous version too. The normalization
            // cannot be reversed without also changing attempts completed after this migration.
        }
    }
}
