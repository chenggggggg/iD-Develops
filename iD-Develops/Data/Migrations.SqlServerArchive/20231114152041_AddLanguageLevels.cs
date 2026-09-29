using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iD_Develops.Data.Migrations
{
    public partial class AddLanguageLevels : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LanguageLevelId",
                table: "Questions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "LanguageLevels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Level = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LanguageLevels", x => x.Id);
                });

            // Seed predefined language levels safely (IDENTITY_INSERT)
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[LanguageLevels]', N'U') IS NOT NULL
BEGIN
    BEGIN TRY
        SET IDENTITY_INSERT [dbo].[LanguageLevels] ON;

        IF NOT EXISTS (SELECT 1 FROM [dbo].[LanguageLevels] WHERE [Id] = 1)
            INSERT INTO [dbo].[LanguageLevels] ([Id], [Level]) VALUES (1, 'A1');

        IF NOT EXISTS (SELECT 1 FROM [dbo].[LanguageLevels] WHERE [Id] = 2)
            INSERT INTO [dbo].[LanguageLevels] ([Id], [Level]) VALUES (2, 'A2');

        IF NOT EXISTS (SELECT 1 FROM [dbo].[LanguageLevels] WHERE [Id] = 3)
            INSERT INTO [dbo].[LanguageLevels] ([Id], [Level]) VALUES (3, '1F');

        IF NOT EXISTS (SELECT 1 FROM [dbo].[LanguageLevels] WHERE [Id] = 4)
            INSERT INTO [dbo].[LanguageLevels] ([Id], [Level]) VALUES (4, '2F');

        IF NOT EXISTS (SELECT 1 FROM [dbo].[LanguageLevels] WHERE [Id] = 5)
            INSERT INTO [dbo].[LanguageLevels] ([Id], [Level]) VALUES (5, '3F');

        IF NOT EXISTS (SELECT 1 FROM [dbo].[LanguageLevels] WHERE [Id] = 6)
            INSERT INTO [dbo].[LanguageLevels] ([Id], [Level]) VALUES (6, '4F');

        IF NOT EXISTS (SELECT 1 FROM [dbo].[LanguageLevels] WHERE [Id] = 7)
            INSERT INTO [dbo].[LanguageLevels] ([Id], [Level]) VALUES (7, 'B1');

        IF NOT EXISTS (SELECT 1 FROM [dbo].[LanguageLevels] WHERE [Id] = 8)
            INSERT INTO [dbo].[LanguageLevels] ([Id], [Level]) VALUES (8, 'B2');

        IF NOT EXISTS (SELECT 1 FROM [dbo].[LanguageLevels] WHERE [Id] = 9)
            INSERT INTO [dbo].[LanguageLevels] ([Id], [Level]) VALUES (9, 'C1');

        IF NOT EXISTS (SELECT 1 FROM [dbo].[LanguageLevels] WHERE [Id] = 10)
            INSERT INTO [dbo].[LanguageLevels] ([Id], [Level]) VALUES (10, 'C2');

        SET IDENTITY_INSERT [dbo].[LanguageLevels] OFF;
    END TRY
    BEGIN CATCH
        BEGIN TRY
            SET IDENTITY_INSERT [dbo].[LanguageLevels] OFF;
        END TRY
        BEGIN CATCH
        END CATCH;

        THROW;
    END CATCH
END
");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_LanguageLevelId",
                table: "Questions",
                column: "LanguageLevelId");

            migrationBuilder.AddForeignKey(
                name: "FK_Questions_LanguageLevels_LanguageLevelId",
                table: "Questions",
                column: "LanguageLevelId",
                principalTable: "LanguageLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Questions_LanguageLevels_LanguageLevelId",
                table: "Questions");

            migrationBuilder.DropTable(
                name: "LanguageLevels");

            migrationBuilder.DropIndex(
                name: "IX_Questions_LanguageLevelId",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "LanguageLevelId",
                table: "Questions");
        }
    }
}
