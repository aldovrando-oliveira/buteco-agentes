using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentDelegationCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_delegations",
                columns: table => new
                {
                    SourceAgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetAgentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_delegations", x => new { x.SourceAgentId, x.TargetAgentId });
                    table.ForeignKey(
                        name: "FK_agent_delegations_agents_SourceAgentId",
                        column: x => x.SourceAgentId,
                        principalTable: "agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_delegations_agents_TargetAgentId",
                        column: x => x.TargetAgentId,
                        principalTable: "agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_delegations_TargetAgentId",
                table: "agent_delegations",
                column: "TargetAgentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_delegations");
        }
    }
}
