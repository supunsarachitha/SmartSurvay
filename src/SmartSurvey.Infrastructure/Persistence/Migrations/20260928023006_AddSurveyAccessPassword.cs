using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSurvey.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyAccessPassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessPasswordHash",
                table: "Surveys",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccessPasswordHash",
                table: "Surveys");
        }
    }
}
