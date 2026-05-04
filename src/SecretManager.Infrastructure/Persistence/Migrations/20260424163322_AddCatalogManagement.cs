using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecretManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DefaultIntegrationMode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "application_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    ManagedNodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_application_assignments_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_application_assignments_environments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "environments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_application_assignments_managed_nodes_ManagedNodeId",
                        column: x => x.ManagedNodeId,
                        principalTable: "managed_nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_application_assignments_node_groups_NodeGroupId",
                        column: x => x.NodeGroupId,
                        principalTable: "node_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "namespaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Path = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_namespaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_namespaces_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "config_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    NamespaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FullPath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ValueType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsSecret = table.Column<bool>(type: "boolean", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultRolloutPolicy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ValidationSchemaJson = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_config_items_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_config_items_namespaces_NamespaceId",
                        column: x => x.NamespaceId,
                        principalTable: "namespaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_application_assignments_ApplicationId_EnvironmentId_NodeGro~",
                table: "application_assignments",
                columns: new[] { "ApplicationId", "EnvironmentId", "NodeGroupId", "ManagedNodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_application_assignments_EnvironmentId",
                table: "application_assignments",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_application_assignments_ManagedNodeId",
                table: "application_assignments",
                column: "ManagedNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_application_assignments_NodeGroupId",
                table: "application_assignments",
                column: "NodeGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_applications_Slug",
                table: "applications",
                column: "Slug",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_config_items_ApplicationId_FullPath",
                table: "config_items",
                columns: new[] { "ApplicationId", "FullPath" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_config_items_NamespaceId",
                table: "config_items",
                column: "NamespaceId");

            migrationBuilder.CreateIndex(
                name: "IX_namespaces_ApplicationId_Path",
                table: "namespaces",
                columns: new[] { "ApplicationId", "Path" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_assignments");

            migrationBuilder.DropTable(
                name: "config_items");

            migrationBuilder.DropTable(
                name: "namespaces");

            migrationBuilder.DropTable(
                name: "applications");
        }
    }
}
