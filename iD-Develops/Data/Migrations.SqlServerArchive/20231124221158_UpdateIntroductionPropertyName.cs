using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class UpdateIntroductionPropertyName : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IntroductionEnglish",
                table: "Exams",
                newName: "IntroductionSecondaryLanguage");

            migrationBuilder.RenameColumn(
                name: "IntroductionDutch",
                table: "Exams",
                newName: "IntroductionPrimaryLanguage");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IntroductionSecondaryLanguage",
                table: "Exams",
                newName: "IntroductionEnglish");

            migrationBuilder.RenameColumn(
                name: "IntroductionPrimaryLanguage",
                table: "Exams",
                newName: "IntroductionDutch");
        }
    }
}
