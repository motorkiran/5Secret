using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecretManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishPipelineImmutableVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "publish_operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    InitiatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangeSummary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publish_operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_publish_operations_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_publish_operations_environments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "environments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_publish_operations_user_accounts_InitiatedByUserId",
                        column: x => x.InitiatedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "published_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    RolloutPolicy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PublishedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SupersedesVersionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_published_versions_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_published_versions_environments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "environments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_published_versions_publish_operations_PublishOperationId",
                        column: x => x.PublishOperationId,
                        principalTable: "publish_operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_published_versions_published_versions_SupersedesVersionId",
                        column: x => x.SupersedesVersionId,
                        principalTable: "published_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_published_versions_user_accounts_PublishedByUserId",
                        column: x => x.PublishedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_publish_operations_ApplicationId",
                table: "publish_operations",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_publish_operations_EnvironmentId_ApplicationId_CreatedAtUtc",
                table: "publish_operations",
                columns: new[] { "EnvironmentId", "ApplicationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_publish_operations_InitiatedByUserId",
                table: "publish_operations",
                column: "InitiatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_published_versions_ApplicationId",
                table: "published_versions",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_published_versions_EnvironmentId_ApplicationId_VersionNumber",
                table: "published_versions",
                columns: new[] { "EnvironmentId", "ApplicationId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_published_versions_PublishedByUserId",
                table: "published_versions",
                column: "PublishedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_published_versions_PublishOperationId",
                table: "published_versions",
                column: "PublishOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_published_versions_SupersedesVersionId",
                table: "published_versions",
                column: "SupersedesVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "published_versions");

            migrationBuilder.DropTable(
                name: "publish_operations");
        }
    }
}
