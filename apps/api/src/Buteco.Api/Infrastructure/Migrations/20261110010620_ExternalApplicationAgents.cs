using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExternalApplicationAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "application_id",
                table: "a2a_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "external_application_agents",
                columns: table => new
                {
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operations = table.Column<List<string>>(type: "text[]", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_application_agents", x => new { x.application_id, x.agent_id });
                    table.CheckConstraint("CK_external_application_agents_operations", "cardinality(operations) > 0");
                    table.ForeignKey(
                        name: "FK_external_application_agents_agents_agent_id",
                        column: x => x.agent_id,
                        principalTable: "agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_external_application_agents_external_applications_applicati~",
                        column: x => x.application_id,
                        principalTable: "external_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_a2a_tasks_application_id",
                table: "a2a_tasks",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "IX_external_application_agents_agent_id",
                table: "external_application_agents",
                column: "agent_id");

            migrationBuilder.AddForeignKey(
                name: "FK_a2a_tasks_external_applications_application_id",
                table: "a2a_tasks",
                column: "application_id",
                principalTable: "external_applications",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_a2a_tasks_external_applications_application_id",
                table: "a2a_tasks");

            migrationBuilder.DropTable(
                name: "external_application_agents");

            migrationBuilder.DropIndex(
                name: "IX_a2a_tasks_application_id",
                table: "a2a_tasks");

            migrationBuilder.DropColumn(
                name: "application_id",
                table: "a2a_tasks");
        }
    }
}
