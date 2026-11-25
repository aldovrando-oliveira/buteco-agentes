using CrossAppTaskStoreCompatibility.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using ApiDbContext = Buteco.Api.Infrastructure.AppDbContext;
using ApiEnabledLlmModel = Buteco.Api.Providers.Entities.EnabledLlmModel;
using WorkerDbContext = Buteco.Workers.Infrastructure.AppDbContext;

namespace CrossAppTaskStoreCompatibility.Tests;

/// <summary>
/// O acordo da lista curada entre os dois contextos (design.md da change
/// modelos-curados-por-provedor, D9; convenção 11): o banco é migrado pelas migrations REAIS do
/// <c>apps/api</c>, com a carga inicial, e o contexto espelho do <c>apps/workers</c> lê um par
/// da carga e um par gravado pelo modelo EF do <c>apps/api</c>, o mesmo que a rota de habilitar
/// usa. Fixture montada com o mesmo modelo dos dois lados não provaria nada.
/// </summary>
public class EnabledLlmModelsCrossAppCompatibilityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .WithDatabase("buteco_cross_app_enabled_models_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    private IServiceScopeFactory _apiScopeFactory = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var apiServices = new ServiceCollection();
        apiServices.AddDbContext<ApiDbContext>(options => options.UseButecoAgentsNpgsql(_postgres.GetConnectionString()));
        _apiScopeFactory = apiServices.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        using var scope = _apiScopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApiDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task WorkersReads_APairFromTheSeed_AndAPairWrittenByTheApi()
    {
        using (var scope = _apiScopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            dbContext.EnabledLlmModels.Add(new ApiEnabledLlmModel("gemini", "gemini-habilitado-pelo-api"));
            await dbContext.SaveChangesAsync();
        }

        await using var workerContext = CreateWorkerContext();
        var pairs = await workerContext.EnabledLlmModels.AsNoTracking()
            .Select(enabled => new { enabled.Provider, enabled.Model })
            .ToListAsync();

        Assert.Contains(pairs, pair => pair.Provider == "openai" && pair.Model == "gpt-5.6-sol");
        Assert.Contains(pairs, pair => pair.Provider == "gemini" && pair.Model == "gemini-habilitado-pelo-api");
        Assert.Equal(10, pairs.Count);
    }

    /// <summary>
    /// O espelho repete a carga (D8) para as fixtures do apps/workers; se ela divergir da do
    /// apps/api, os testes de lá rodariam sobre uma lista que produção nunca teve.
    /// </summary>
    [Fact]
    public void TheMirrorMigration_HasTheSameSeedAsTheApiOne() =>
        Assert.Equal(
            Buteco.Api.Infrastructure.Migrations.AddLlmEnabledModels.SeedSql,
            Buteco.Workers.Infrastructure.Migrations.AddLlmEnabledModels.SeedSql);

    private WorkerDbContext CreateWorkerContext() =>
        new(new DbContextOptionsBuilder<WorkerDbContext>().UseButecoAgentsNpgsql(_postgres.GetConnectionString()).Options);
}
