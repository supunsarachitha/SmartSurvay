using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSurvey.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStarterTemplatesSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ProvideStarterTemplates",
                table: "PlatformSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true); // existing settings rows keep the new default: templates on
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProvideStarterTemplates",
                table: "PlatformSettings");
        }
    }
}
