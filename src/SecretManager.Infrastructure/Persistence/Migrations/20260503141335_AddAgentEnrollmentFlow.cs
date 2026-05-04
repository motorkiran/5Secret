using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecretManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentEnrollmentFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_enrollment_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagedNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    IssuedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsumedByAgentId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_enrollment_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_enrollment_tokens_managed_nodes_ManagedNodeId",
                        column: x => x.ManagedNodeId,
                        principalTable: "managed_nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_enrollment_tokens_user_accounts_IssuedByUserId",
                        column: x => x.IssuedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "agent_registrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagedNodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    EnrolledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_registrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agent_registrations_managed_nodes_ManagedNodeId",
                        column: x => x.ManagedNodeId,
                        principalTable: "managed_nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_enrollment_tokens_IssuedByUserId",
                table: "agent_enrollment_tokens",
                column: "IssuedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_enrollment_tokens_ManagedNodeId",
                table: "agent_enrollment_tokens",
                column: "ManagedNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_registrations_ManagedNodeId",
                table: "agent_registrations",
                column: "ManagedNodeId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_enrollment_tokens");

            migrationBuilder.DropTable(
                name: "agent_registrations");
        }
    }
}
