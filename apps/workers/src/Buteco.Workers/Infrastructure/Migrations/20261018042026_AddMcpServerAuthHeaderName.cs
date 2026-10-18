using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Workers.Infrastructure.Migrations
{
    /// <summary>
    /// Espelho da migração de mesmo nome de <c>apps/api</c> (change
    /// mcp-autenticacao-header-customizado): <b>nunca é aplicada em runtime</b>.
    /// Só <c>apps/api</c> migra o banco compartilhado (o <c>migrator</c> do compose
    /// roda os bundles de <c>apps/api</c> e <c>apps/inbox</c>,
    /// <c>deploy/migrate/entrypoint.sh</c>), e este contexto nunca chama
    /// <c>Database.MigrateAsync</c> — ver o comentário de <c>AppDbContext</c>.
    /// Existe para o ferramental do EF Core e para o banco das fixtures de teste de
    /// <c>apps/workers</c>, que migram por este contexto. Precedente:
    /// <c>20260803021548_AddMcpServerCatalog</c> (change apps-workers-execucao-mcp).
    /// Só acrescenta a coluna: a passagem do modelo para <c>McpServerAuthType?</c>
    /// não altera a coluna <c>AuthType</c>, que continua <c>NOT NULL</c>.
    /// </summary>
    public partial class AddMcpServerAuthHeaderName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthHeaderName",
                table: "mcp_servers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthHeaderName",
                table: "mcp_servers");
        }
    }
}
