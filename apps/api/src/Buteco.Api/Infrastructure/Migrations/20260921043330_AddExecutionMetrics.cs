using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "task_executions",
                columns: table => new
                {
                    TaskId = table.Column<string>(type: "text", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContextId = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: true),
                    Model = table.Column<string>(type: "text", nullable: true),
                    Origin = table.Column<string>(type: "text", nullable: false),
                    SourceAgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceTaskId = table.Column<string>(type: "text", nullable: true),
                    DelegationDepth = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LockAcquiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TerminalState = table.Column<string>(type: "text", nullable: true),
                    FailurePhase = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_executions", x => x.TaskId);
                });

            migrationBuilder.CreateTable(
                name: "delegation_outcomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceTaskId = table.Column<string>(type: "text", nullable: false),
                    SourceAgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetAgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetTaskId = table.Column<string>(type: "text", nullable: true),
                    Outcome = table.Column<string>(type: "text", nullable: false),
                    LastObservedTargetState = table.Column<string>(type: "text", nullable: true),
                    LastObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SuccessfulReadCount = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delegation_outcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delegation_outcomes_task_executions_SourceTaskId",
                        column: x => x.SourceTaskId,
                        principalTable: "task_executions",
                        principalColumn: "TaskId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "provider_calls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    DurationMs = table.Column<double>(type: "double precision", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: true),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    CachedInputTokens = table.Column<long>(type: "bigint", nullable: true),
                    Failed = table.Column<bool>(type: "boolean", nullable: false),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_calls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_provider_calls_task_executions_TaskId",
                        column: x => x.TaskId,
                        principalTable: "task_executions",
                        principalColumn: "TaskId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_delegation_outcomes_SourceAgentId",
                table: "delegation_outcomes",
                column: "SourceAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_delegation_outcomes_SourceTaskId",
                table: "delegation_outcomes",
                column: "SourceTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_delegation_outcomes_TargetAgentId",
                table: "delegation_outcomes",
                column: "TargetAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_provider_calls_TaskId",
                table: "provider_calls",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_task_executions_AgentId",
                table: "task_executions",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_task_executions_StartedAt",
                table: "task_executions",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delegation_outcomes");

            migrationBuilder.DropTable(
                name: "provider_calls");

            migrationBuilder.DropTable(
                name: "task_executions");
        }
    }
}
