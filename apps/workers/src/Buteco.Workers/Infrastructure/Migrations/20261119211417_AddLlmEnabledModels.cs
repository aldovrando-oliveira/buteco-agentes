using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Workers.Infrastructure.Migrations
{
    /// <summary>
    /// Espelho da migração de mesmo nome de <c>apps/api</c> (change modelos-curados-por-provedor,
    /// D8): <b>nunca é aplicada em runtime</b>. Só <c>apps/api</c> migra o banco compartilhado (o
    /// <c>migrator</c> do compose, <c>deploy/migrate/entrypoint.sh</c>), e este contexto nunca chama
    /// <c>Database.MigrateAsync</c>. Existe para o ferramental do EF Core e para o banco das fixtures
    /// de teste de <c>apps/workers</c>, que migram por este contexto. Precedente:
    /// <c>20261018042026_AddMcpServerAuthHeaderName</c>.
    ///
    /// <para>
    /// <b>Repete a criação E a carga inicial</b>: os testes de delegação e de execução daqui gravam
    /// agentes com <c>gpt-5.6-sol</c> e <c>claude-opus-5</c>, e um espelho sem a carga os faria
    /// reprovar sem defeito nenhum. O texto de <see cref="SeedSql"/> é conferido contra o do
    /// <c>apps/api</c> pelo acordo de <c>tests/CrossAppTaskStoreCompatibility.Tests</c> (D9).
    /// </para>
    /// </summary>
    public partial class AddLlmEnabledModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "llm_enabled_models",
                columns: table => new
                {
                    Provider = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_llm_enabled_models", x => new { x.Provider, x.Model });
                });

            migrationBuilder.Sql(SeedSql);
        }

        /// <summary>A mesma carga do apps/api, byte a byte (conferida pelo acordo, D9).</summary>
        public const string SeedSql = """
            INSERT INTO llm_enabled_models ("Provider", "Model") VALUES
                ('openai', 'gpt-5.6-sol'),
                ('openai', 'gpt-5.6-terra'),
                ('openai', 'gpt-5.6-luna'),
                ('anthropic', 'claude-opus-5'),
                ('anthropic', 'claude-sonnet-5'),
                ('anthropic', 'claude-haiku-4-5'),
                ('gemini', 'gemini-3.6-flash'),
                ('gemini', 'gemini-3.5-flash'),
                ('gemini', 'gemini-3.1-flash-lite')
            ON CONFLICT DO NOTHING;

            INSERT INTO llm_enabled_models ("Provider", "Model")
            SELECT DISTINCT "Provider", "Model"
            FROM agents
            WHERE "Provider" IN ('openai', 'anthropic', 'gemini') AND "Model" IS NOT NULL
            ON CONFLICT DO NOTHING;
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "llm_enabled_models");
        }
    }
}
