using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Inbox.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueOpenSessionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Saneamento antes do índice, não depois (design.md,
            // "Migração de dados"): nenhuma Session jamais teve ClosedAt
            // escrito, então todo Contact com mais de uma Session histórica
            // viola a unicidade abaixo sem este passo. Fecha toda Session
            // exceto a mais recente por Contact, com ClosedAt = StartedAt da
            // Session seguinte (não o instante da migração) — é o valor
            // correto de domínio, e não depende de quando a migration roda.
            migrationBuilder.Sql("""
                WITH ordered AS (
                  SELECT "Id",
                         LEAD("StartedAt") OVER (PARTITION BY "ContactId" ORDER BY "StartedAt") AS next_started_at
                  FROM sessions
                )
                UPDATE sessions s
                SET "ClosedAt" = o.next_started_at
                FROM ordered o
                WHERE s."Id" = o."Id" AND o.next_started_at IS NOT NULL AND s."ClosedAt" IS NULL;
                """);

            migrationBuilder.DropIndex(
                name: "IX_sessions_ContactId",
                table: "sessions");

            migrationBuilder.CreateIndex(
                name: "IX_sessions_ContactId",
                table: "sessions",
                column: "ContactId",
                unique: true,
                filter: "\"ClosedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sessions_ContactId",
                table: "sessions");

            migrationBuilder.CreateIndex(
                name: "IX_sessions_ContactId",
                table: "sessions",
                column: "ContactId");
        }
    }
}
