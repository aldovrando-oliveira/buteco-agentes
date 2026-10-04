using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeIndexingRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "knowledge_indexing_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentRevision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_indexing_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_knowledge_indexing_requests_knowledge_documents_KnowledgeDo~",
                        column: x => x.KnowledgeDocumentId,
                        principalTable: "knowledge_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_indexing_requests_CreatedAt_Id",
                table: "knowledge_indexing_requests",
                columns: new[] { "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_indexing_requests_KnowledgeDocumentId",
                table: "knowledge_indexing_requests",
                column: "KnowledgeDocumentId");

            // Recuperação dos órfãos que já existem (design.md da change
            // indexacao-sem-job-orfao, D6): um pedido por documento em Pending, com a
            // revisão corrente. Pending com mensagem na fila e Pending órfão são a
            // mesma linha (D1), então os dois ganham pedido: o primeiro é indexado
            // duas vezes, na mesma revisão, e termina com o conjunto de fragmentos de
            // uma indexação só — custo aceito, uma vez, nesta implantação. Indexing,
            // Indexed e Failed ficam de fora.
            migrationBuilder.Sql("""
                INSERT INTO knowledge_indexing_requests ("Id", "KnowledgeDocumentId", "ContentRevision", "CreatedAt")
                SELECT gen_random_uuid(), "Id", "ContentRevision", now()
                FROM knowledge_documents
                WHERE "IndexingStatus" = 'Pending';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "knowledge_indexing_requests");
        }
    }
}
