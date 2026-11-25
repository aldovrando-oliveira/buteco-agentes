using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;

namespace Buteco.Api.Tests.Providers;

/// <summary>
/// A carga inicial da lista curada (design.md da change modelos-curados-por-provedor, D3; spec
/// <c>llm-model-curation</c>, "Carga inicial preserva todo agente que executa"). Mesmo molde de
/// <c>AgentDescriptionAndSkillsMigrationTests</c>: aplica as migrations até a anterior, grava
/// os agentes por SQL (como a edição manual que a carga cobre) e só então aplica a nova.
///
/// <para>
/// <b>O banco povoado é a precondição, e um caso a afirma</b> (convenção 15, vacuidade): sobre
/// banco sem agentes, o <c>SELECT DISTINCT</c> dos pares em uso devolve vazio, e um guarda que só
/// rodasse ali ficaria verde com e sem a parte da carga que ele existe para provar.
/// </para>
/// </summary>
public class LlmEnabledModelsMigrationTests : IAsyncLifetime
{
    private const string PreviousMigrationId = "20261113033350_AddUserFederatedIdentities";

    private static readonly (string Provider, string Model)[] StaticCatalog =
    [
        ("anthropic", "claude-haiku-4-5"),
        ("anthropic", "claude-opus-5"),
        ("anthropic", "claude-sonnet-5"),
        ("gemini", "gemini-3.1-flash-lite"),
        ("gemini", "gemini-3.5-flash"),
        ("gemini", "gemini-3.6-flash"),
        ("openai", "gpt-5.6-luna"),
        ("openai", "gpt-5.6-sol"),
        ("openai", "gpt-5.6-terra"),
    ];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .WithDatabase("buteco_agents_enabled_models_migration_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task OnADatabaseWithoutAgents_TheListIsExactlyTheNineOfTheStaticCatalog()
    {
        await MigrateAsync(PreviousMigrationId);
        await MigrateAsync(null);

        Assert.Equal(StaticCatalog, await EnabledPairsAsync());
    }

    [Fact]
    public async Task OnAPopulatedDatabase_PairsInUseAreEnabledOnce_WithoutNullsAndWithoutUnknownProviders()
    {
        await MigrateAsync(PreviousMigrationId);

        // Um par fora dos nove, em dois agentes; um agente legado de nulos; um agente com
        // provedor fora de LlmProviders.All. Todos por SQL, como a edição manual.
        await InsertAgentAsync("openai", "gpt-4o-mini");
        await InsertAgentAsync("openai", "gpt-4o-mini");
        await InsertAgentAsync(null, null);
        await InsertAgentAsync("mistral", "mistral-large");
        await InsertAgentAsync("anthropic", "claude-opus-5");

        // A precondição do guarda: o banco está povoado ANTES da migration, com o par fora dos
        // nove presente. Sem isto, o caso poderia passar sobre banco vazio.
        Assert.Equal(5, await CountAgentsAsync());

        await MigrateAsync(null);

        var pairs = await EnabledPairsAsync();
        Assert.Equal(
            StaticCatalog.Append((Provider: "openai", Model: "gpt-4o-mini")).OrderBy(pair => pair.Provider, StringComparer.Ordinal).ThenBy(pair => pair.Model, StringComparer.Ordinal),
            pairs.OrderBy(pair => pair.Provider, StringComparer.Ordinal).ThenBy(pair => pair.Model, StringComparer.Ordinal));
        Assert.Single(pairs, pair => pair.Model == "gpt-4o-mini");
        Assert.DoesNotContain(pairs, pair => pair.Provider == "mistral");
    }

    private DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(_postgres.GetConnectionString()).Options;

    private async Task MigrateAsync(string? target)
    {
        await using var dbContext = new AppDbContext(Options());
        await dbContext.GetService<IMigrator>().MigrateAsync(target!, CancellationToken.None);
    }

    private async Task InsertAgentAsync(string? provider, string? model)
    {
        await using var dbContext = new AppDbContext(Options());
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO agents ("Id", "Name", "Instructions", "IsActive", "Provider", "Model", "CreatedAt", "UpdatedAt")
            VALUES ({Guid.NewGuid()}, 'Agente Pré-Existente', 'Instruções.', true, {provider}, {model}, now(), now())
            """);
    }

    private async Task<int> CountAgentsAsync()
    {
        await using var dbContext = new AppDbContext(Options());
        return await dbContext.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM agents").SingleAsync();
    }

    private async Task<List<(string Provider, string Model)>> EnabledPairsAsync()
    {
        await using var dbContext = new AppDbContext(Options());
        var rows = await dbContext.EnabledLlmModels.AsNoTracking().ToListAsync();
        return rows
            .Select(row => (row.Provider, row.Model))
            .OrderBy(pair => pair.Provider, StringComparer.Ordinal)
            .ThenBy(pair => pair.Model, StringComparer.Ordinal)
            .ToList();
    }
}
