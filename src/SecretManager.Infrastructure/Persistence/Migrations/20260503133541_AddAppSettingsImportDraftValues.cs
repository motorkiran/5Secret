using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecretManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppSettingsImportDraftValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "draft_values",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeType = table.Column<int>(type: "integer", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValueJson = table.Column<string>(type: "text", nullable: false),
                    IsSecret = table.Column<bool>(type: "boolean", nullable: false),
                    ChangeNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_draft_values", x => x.Id);
                    table.ForeignKey(
                        name: "FK_draft_values_config_items_ConfigItemId",
                        column: x => x.ConfigItemId,
                        principalTable: "config_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_draft_values_user_accounts_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "user_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_draft_values_ConfigItemId_ScopeType_ScopeId",
                table: "draft_values",
                columns: new[] { "ConfigItemId", "ScopeType", "ScopeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_draft_values_UpdatedByUserId",
                table: "draft_values",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "draft_values");
        }
    }
}
