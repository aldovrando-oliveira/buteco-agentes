using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeDocumentEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "knowledge_document_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeBaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentTitle = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    ContentChanged = table.Column<bool>(type: "boolean", nullable: true),
                    TitleChanged = table.Column<bool>(type: "boolean", nullable: true),
                    Author = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_document_events", x => x.Id);
                    table.CheckConstraint("CK_knowledge_document_events_change_detail", "(\"Type\" = 'Updated' AND \"ContentChanged\" IS NOT NULL AND \"TitleChanged\" IS NOT NULL AND (\"ContentChanged\" OR \"TitleChanged\")) OR (\"Type\" <> 'Updated' AND \"ContentChanged\" IS NULL AND \"TitleChanged\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_knowledge_document_events_knowledge_bases_KnowledgeBaseId",
                        column: x => x.KnowledgeBaseId,
                        principalTable: "knowledge_bases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_document_events_KnowledgeBaseId_OccurredAt_Id",
                table: "knowledge_document_events",
                columns: new[] { "KnowledgeBaseId", "OccurredAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "knowledge_document_events");
        }
    }
}
