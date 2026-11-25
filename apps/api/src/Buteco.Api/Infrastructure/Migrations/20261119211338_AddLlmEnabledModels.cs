using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buteco.Api.Infrastructure.Migrations
{
    /// <summary>
    /// A lista curada de modelos por provedor e a carga inicial (design.md da change
    /// modelos-curados-por-provedor, D1 e D3).
    ///
    /// <para>
    /// <b>A carga é os nove pares do catálogo estático que esta change remove
    /// (<c>ProviderModelCatalog</c>) ∪ os pares distintos gravados em agentes</b>, para que
    /// nenhum agente que executava antes do deploy passe a ser recusado, inclusive o editado por
    /// SQL. O par em uso entra mesmo com o provedor sem chave: a carga não decide disponibilidade
    /// de ambiente.
    /// </para>
    ///
    /// <para>
    /// <b>O <c>SELECT</c> só leva provedores de <c>LlmProviders.All</c></b>: um agente com outro
    /// provedor só existe por SQL manual, já é recusado como <c>ProviderNotConfigured</c>, e o par
    /// dele ficaria preso na lista, porque nem o <c>DELETE</c> da curadoria alcança provedor
    /// desconhecido. Os ids e os nove pares ficam literais aqui: migration é histórica, e um
    /// provedor ou modelo novo não reescreve a carga de antes.
    /// </para>
    ///
    /// <para>
    /// O espelho de mesmo nome do <c>apps/workers</c> repete a criação e a carga (D8), para as
    /// fixtures de teste de lá; em produção só esta roda.
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

        /// <summary>A carga inicial (D3). Pública para o acordo de tests/CrossAppTaskStoreCompatibility conferir a cópia do espelho.</summary>
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
