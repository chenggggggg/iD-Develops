using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    /// <inheritdoc />
    public partial class PreservePurchasedCreditScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserCreditLots_CourseClasses_CourseClassId",
                table: "UserCreditLots");

            migrationBuilder.DropForeignKey(
                name: "FK_UserCreditLots_Courses_CourseId",
                table: "UserCreditLots");

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "UserCreditLots",
                type: "text",
                nullable: false,
                defaultValue: "Global");

            migrationBuilder.Sql(
                """
                UPDATE "UserCreditLots"
                SET "Scope" = CASE
                    WHEN "CourseClassId" IS NOT NULL THEN 'CourseClass'
                    WHEN "CourseId" IS NOT NULL THEN 'Course'
                    ELSE 'Global'
                END;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_UserCreditLots_CourseClasses_CourseClassId",
                table: "UserCreditLots",
                column: "CourseClassId",
                principalTable: "CourseClasses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_UserCreditLots_Courses_CourseId",
                table: "UserCreditLots",
                column: "CourseId",
                principalTable: "Courses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserCreditLots_CourseClasses_CourseClassId",
                table: "UserCreditLots");

            migrationBuilder.DropForeignKey(
                name: "FK_UserCreditLots_Courses_CourseId",
                table: "UserCreditLots");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "UserCreditLots");

            migrationBuilder.AddForeignKey(
                name: "FK_UserCreditLots_CourseClasses_CourseClassId",
                table: "UserCreditLots",
                column: "CourseClassId",
                principalTable: "CourseClasses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserCreditLots_Courses_CourseId",
                table: "UserCreditLots",
                column: "CourseId",
                principalTable: "Courses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
