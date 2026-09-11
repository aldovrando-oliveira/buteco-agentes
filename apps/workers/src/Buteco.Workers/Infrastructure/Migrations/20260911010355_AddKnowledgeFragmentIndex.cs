using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Buteco.Workers.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeFragmentIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "knowledge_documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FragmentCount",
                table: "knowledge_documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "IndexingAttempts",
                table: "knowledge_documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastAttemptAt",
                table: "knowledge_documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "knowledge_fragments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeBaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(4096)", nullable: false),
                    EmbeddingProvider = table.Column<string>(type: "text", nullable: false),
                    EmbeddingModel = table.Column<string>(type: "text", nullable: false),
                    EmbeddingDimensions = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_fragments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_knowledge_fragments_knowledge_documents_KnowledgeDocumentId",
                        column: x => x.KnowledgeDocumentId,
                        principalTable: "knowledge_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_fragments_KnowledgeBaseId",
                table: "knowledge_fragments",
                column: "KnowledgeBaseId");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_fragments_KnowledgeDocumentId",
                table: "knowledge_fragments",
                column: "KnowledgeDocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "knowledge_fragments");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "FragmentCount",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "IndexingAttempts",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                table: "knowledge_documents");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
