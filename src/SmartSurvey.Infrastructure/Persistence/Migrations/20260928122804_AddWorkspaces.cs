using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartSurvey.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaces : Migration
    {
        /// <summary>Id of the workspace that receives the data of an upgraded v1.0 database.</summary>
        public const string DefaultWorkspaceId = "d3fa0175-0000-4000-8000-000000000001";

        /// <summary>Tables whose existing rows move into the default workspace (all rows: v1.0 had no super admins).</summary>
        private static readonly string[] WorkspaceTables =
        [
            "Surveys", "SurveySections", "Questions", "QuestionOptions", "LogicRules", "LogicConditions",
            "Responses", "Answers", "AnswerSelections", "Reports", "ReportWidgets", "AuditLogs", "AspNetUsers",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "SurveySections",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Surveys",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Responses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "ReportWidgets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Reports",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Questions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "QuestionOptions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "LogicRules",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "LogicConditions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "AuditLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "AspNetUsers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "AnswerSelections",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "Answers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "PlatformSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllowWorkspaceSignup = table.Column<bool>(type: "boolean", nullable: false),
                    RequireWorkspaceApproval = table.Column<bool>(type: "boolean", nullable: false),
                    SupportEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    StatusChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AllowSelfRegistration = table.Column<bool>(type: "boolean", nullable: false),
                    ShowPublicSurveyList = table.Column<bool>(type: "boolean", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                });

            // ----- Upgrade from v1.0 (single tenant) ---------------------------------------------------
            // Existing data becomes "Default workspace" and its users become its members (admins stay
            // admins). Fresh databases get no workspace here. Runs before the foreign keys are created.
            migrationBuilder.Sql($"""
                INSERT INTO "Workspaces" ("Id", "Name", "Slug", "Status", "AllowSelfRegistration", "ShowPublicSurveyList", "CreatedAt")
                SELECT '{DefaultWorkspaceId}', 'Default workspace', 'default', 'Active', TRUE, TRUE, now()
                WHERE EXISTS (SELECT 1 FROM "AspNetUsers") OR EXISTS (SELECT 1 FROM "Surveys") OR EXISTS (SELECT 1 FROM "AuditLogs");
                """);

            foreach (var table in WorkspaceTables)
            {
                migrationBuilder.Sql($"""UPDATE "{table}" SET "WorkspaceId" = '{DefaultWorkspaceId}';""");
            }

            migrationBuilder.CreateIndex(
                name: "IX_Surveys_WorkspaceId",
                table: "Surveys",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Responses_WorkspaceId",
                table: "Responses",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_WorkspaceId",
                table: "Reports",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_WorkspaceId_Timestamp",
                table: "AuditLogs",
                columns: new[] { "WorkspaceId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_WorkspaceId",
                table: "AspNetUsers",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_Slug",
                table: "Workspaces",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_Status",
                table: "Workspaces",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Workspaces_WorkspaceId",
                table: "AspNetUsers",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Workspaces_WorkspaceId",
                table: "AuditLogs",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Reports_Workspaces_WorkspaceId",
                table: "Reports",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Responses_Workspaces_WorkspaceId",
                table: "Responses",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Surveys_Workspaces_WorkspaceId",
                table: "Surveys",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Workspaces_WorkspaceId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Workspaces_WorkspaceId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_Reports_Workspaces_WorkspaceId",
                table: "Reports");

            migrationBuilder.DropForeignKey(
                name: "FK_Responses_Workspaces_WorkspaceId",
                table: "Responses");

            migrationBuilder.DropForeignKey(
                name: "FK_Surveys_Workspaces_WorkspaceId",
                table: "Surveys");

            migrationBuilder.DropTable(
                name: "PlatformSettings");

            migrationBuilder.DropTable(
                name: "Workspaces");

            migrationBuilder.DropIndex(
                name: "IX_Surveys_WorkspaceId",
                table: "Surveys");

            migrationBuilder.DropIndex(
                name: "IX_Responses_WorkspaceId",
                table: "Responses");

            migrationBuilder.DropIndex(
                name: "IX_Reports_WorkspaceId",
                table: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_WorkspaceId_Timestamp",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_WorkspaceId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "SurveySections");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Surveys");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Responses");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "ReportWidgets");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "QuestionOptions");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "LogicRules");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "LogicConditions");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "AnswerSelections");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Answers");
        }
    }
}
