using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddA2ATasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "a2a_tasks",
                columns: table => new
                {
                    task_id = table.Column<string>(type: "text", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    context_id = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    status_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_a2a_tasks", x => x.task_id);
                    table.ForeignKey(
                        name: "FK_a2a_tasks_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_a2a_tasks_agent_id",
                table: "a2a_tasks",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "IX_a2a_tasks_context_id",
                table: "a2a_tasks",
                column: "context_id");

            migrationBuilder.CreateIndex(
                name: "IX_a2a_tasks_state",
                table: "a2a_tasks",
                column: "state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "a2a_tasks");
        }
    }
}
