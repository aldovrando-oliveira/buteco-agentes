using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddingMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "knowledge_indexing_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeBaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentRevision = table.Column<int>(type: "integer", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<string>(type: "text", nullable: false),
                    FailurePhase = table.Column<string>(type: "text", nullable: true),
                    FragmentCount = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_indexing_attempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "embedding_calls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    KnowledgeIndexingAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    TaskId = table.Column<string>(type: "text", nullable: true),
                    KnowledgeBaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: false),
                    Dimensions = table.Column<int>(type: "integer", nullable: false),
                    InputCount = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<double>(type: "double precision", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: true),
                    Failed = table.Column<bool>(type: "boolean", nullable: false),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_embedding_calls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_embedding_calls_knowledge_indexing_attempts_KnowledgeIndexi~",
                        column: x => x.KnowledgeIndexingAttemptId,
                        principalTable: "knowledge_indexing_attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_embedding_calls_task_executions_TaskId",
                        column: x => x.TaskId,
                        principalTable: "task_executions",
                        principalColumn: "TaskId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_embedding_calls_KnowledgeIndexingAttemptId",
                table: "embedding_calls",
                column: "KnowledgeIndexingAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_embedding_calls_TaskId",
                table: "embedding_calls",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_indexing_attempts_KnowledgeDocumentId",
                table: "knowledge_indexing_attempts",
                column: "KnowledgeDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_indexing_attempts_StartedAt",
                table: "knowledge_indexing_attempts",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "embedding_calls");

            migrationBuilder.DropTable(
                name: "knowledge_indexing_attempts");
        }
    }
}
