using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecretManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentHeartbeatStatusTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentPublishedVersionId",
                table: "agent_registrations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentVersionNumber",
                table: "agent_registrations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HealthStatus",
                table: "agent_registrations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSeenAtUtc",
                table: "agent_registrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_registrations_CurrentPublishedVersionId",
                table: "agent_registrations",
                column: "CurrentPublishedVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_agent_registrations_published_versions_CurrentPublishedVers~",
                table: "agent_registrations",
                column: "CurrentPublishedVersionId",
                principalTable: "published_versions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_agent_registrations_published_versions_CurrentPublishedVers~",
                table: "agent_registrations");

            migrationBuilder.DropIndex(
                name: "IX_agent_registrations_CurrentPublishedVersionId",
                table: "agent_registrations");

            migrationBuilder.DropColumn(
                name: "CurrentPublishedVersionId",
                table: "agent_registrations");

            migrationBuilder.DropColumn(
                name: "CurrentVersionNumber",
                table: "agent_registrations");

            migrationBuilder.DropColumn(
                name: "HealthStatus",
                table: "agent_registrations");

            migrationBuilder.DropColumn(
                name: "LastSeenAtUtc",
                table: "agent_registrations");
        }
    }
}
