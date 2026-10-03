using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeBaseSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_knowledge_documents_knowledge_bases_KnowledgeBaseId",
                table: "knowledge_documents");

            migrationBuilder.AddColumn<string>(
                name: "ExternalRef",
                table: "knowledge_documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalVersion",
                table: "knowledge_documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KnowledgeBaseContentMode",
                table: "knowledge_documents",
                type: "text",
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<string>(
                name: "ContentMode",
                table: "knowledge_bases",
                type: "text",
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSyncCompletedAt",
                table: "knowledge_bases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSyncErrorCode",
                table: "knowledge_bases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSyncErrorDetail",
                table: "knowledge_bases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSyncFinishedAt",
                table: "knowledge_bases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SyncFailingSince",
                table: "knowledge_bases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncFolderId",
                table: "knowledge_bases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncFolderName",
                table: "knowledge_bases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncFolderUrl",
                table: "knowledge_bases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncIgnoredFiles",
                table: "knowledge_bases",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncProvider",
                table: "knowledge_bases",
                type: "text",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_knowledge_bases_Id_ContentMode",
                table: "knowledge_bases",
                columns: new[] { "Id", "ContentMode" });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_documents_KnowledgeBaseId_ExternalRef",
                table: "knowledge_documents",
                columns: new[] { "KnowledgeBaseId", "ExternalRef" },
                unique: true,
                filter: "\"ExternalRef\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_documents_KnowledgeBaseId_KnowledgeBaseContentMode",
                table: "knowledge_documents",
                columns: new[] { "KnowledgeBaseId", "KnowledgeBaseContentMode" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_knowledge_documents_external_ref",
                table: "knowledge_documents",
                sql: "(\"KnowledgeBaseContentMode\" = 'Synced') = (\"ExternalRef\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_knowledge_documents_external_version",
                table: "knowledge_documents",
                sql: "(\"ExternalRef\" IS NULL) = (\"ExternalVersion\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_bases_SyncProvider_SyncFolderId",
                table: "knowledge_bases",
                columns: new[] { "SyncProvider", "SyncFolderId" },
                unique: true,
                filter: "\"ContentMode\" = 'Synced'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_knowledge_bases_content_mode",
                table: "knowledge_bases",
                sql: "\"ContentMode\" IN ('Manual', 'Synced')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_knowledge_bases_sync_error_detail",
                table: "knowledge_bases",
                sql: "\"LastSyncErrorDetail\" IS NULL OR \"LastSyncErrorCode\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_knowledge_bases_sync_failing_since",
                table: "knowledge_bases",
                sql: "(\"SyncFailingSince\" IS NULL) = (\"LastSyncErrorCode\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_knowledge_bases_sync_source",
                table: "knowledge_bases",
                sql: "(\"ContentMode\" = 'Synced' AND \"SyncProvider\" IS NOT NULL AND \"SyncFolderId\" IS NOT NULL AND \"SyncFolderName\" IS NOT NULL AND \"SyncFolderUrl\" IS NOT NULL) OR (\"ContentMode\" = 'Manual' AND \"SyncProvider\" IS NULL AND \"SyncFolderId\" IS NULL AND \"SyncFolderName\" IS NULL AND \"SyncFolderUrl\" IS NULL AND \"LastSyncCompletedAt\" IS NULL AND \"LastSyncFinishedAt\" IS NULL AND \"LastSyncErrorCode\" IS NULL AND \"LastSyncErrorDetail\" IS NULL AND \"SyncFailingSince\" IS NULL AND \"SyncIgnoredFiles\" IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_knowledge_documents_knowledge_bases_KnowledgeBaseId_Knowled~",
                table: "knowledge_documents",
                columns: new[] { "KnowledgeBaseId", "KnowledgeBaseContentMode" },
                principalTable: "knowledge_bases",
                principalColumns: new[] { "Id", "ContentMode" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_knowledge_documents_knowledge_bases_KnowledgeBaseId_Knowled~",
                table: "knowledge_documents");

            migrationBuilder.DropIndex(
                name: "IX_knowledge_documents_KnowledgeBaseId_ExternalRef",
                table: "knowledge_documents");

            migrationBuilder.DropIndex(
                name: "IX_knowledge_documents_KnowledgeBaseId_KnowledgeBaseContentMode",
                table: "knowledge_documents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_knowledge_documents_external_ref",
                table: "knowledge_documents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_knowledge_documents_external_version",
                table: "knowledge_documents");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_knowledge_bases_Id_ContentMode",
                table: "knowledge_bases");

            migrationBuilder.DropIndex(
                name: "IX_knowledge_bases_SyncProvider_SyncFolderId",
                table: "knowledge_bases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_knowledge_bases_content_mode",
                table: "knowledge_bases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_knowledge_bases_sync_error_detail",
                table: "knowledge_bases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_knowledge_bases_sync_failing_since",
                table: "knowledge_bases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_knowledge_bases_sync_source",
                table: "knowledge_bases");

            migrationBuilder.DropColumn(
                name: "ExternalRef",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "ExternalVersion",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "KnowledgeBaseContentMode",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "ContentMode",
                table: "knowledge_bases");

            migrationBuilder.DropColumn(
                name: "LastSyncCompletedAt",
                table: "knowledge_bases");

            migrationBuilder.DropColumn(
                name: "LastSyncErrorCode",
                table: "knowledge_bases");

            migrationBuilder.DropColumn(
                name: "LastSyncErrorDetail",
                table: "knowledge_bases");

            migrationBuilder.DropColumn(
                name: "LastSyncFinishedAt",
                table: "knowledge_bases");

            migrationBuilder.DropColumn(
                name: "SyncFailingSince",
                table: "knowledge_bases");

            migrationBuilder.DropColumn(
                name: "SyncFolderId",
                table: "knowledge_bases");

            migrationBuilder.DropColumn(
                name: "SyncFolderName",
                table: "knowledge_bases");

            migrationBuilder.DropColumn(
                name: "SyncFolderUrl",
                table: "knowledge_bases");

            migrationBuilder.DropColumn(
                name: "SyncIgnoredFiles",
                table: "knowledge_bases");

            migrationBuilder.DropColumn(
                name: "SyncProvider",
                table: "knowledge_bases");

            migrationBuilder.AddForeignKey(
                name: "FK_knowledge_documents_knowledge_bases_KnowledgeBaseId",
                table: "knowledge_documents",
                column: "KnowledgeBaseId",
                principalTable: "knowledge_bases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
