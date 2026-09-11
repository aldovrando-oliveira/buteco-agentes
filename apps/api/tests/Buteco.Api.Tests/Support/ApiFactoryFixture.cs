using Buteco.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Buteco.Api.Tests.Support;
using Buteco.Api.Knowledge.Indexing;

namespace Buteco.Api.Tests.Support;

public class ApiFactoryFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Url pública usada pelos testes. Fixada aqui pela mesma razão dos
    // provedores logo abaixo: appsettings.Development.json é ajustado
    // livremente por quem desenvolve e não deveria ser pré-requisito de
    // nenhum teste. Sobrescrevível para exercitar o caso de ausência.
    public const string PublicBaseUrl = "https://api.exemplo.test";

    protected virtual string? ConfiguredPublicBaseUrl => PublicBaseUrl;

    // Compatibilidade com testes que já referenciavam estas constantes
    // diretamente nesta fixture — mesmos valores de TestAuthentication.
    public const string KnownOperatorUsername = TestAuthentication.KnownOperatorUsername;
    public const string KnownOperatorPassword = TestAuthentication.KnownOperatorPassword;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .WithDatabase("buteco_agents_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Garante "só openai configurado" como precondição dos testes,
        // independente do que appsettings.Development.json tiver localmente
        // (esse arquivo é ajustado livremente por quem desenvolve, para uso
        // manual da API — não deveria ser pré-requisito de nenhum teste).
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Anthropic:ApiKey"] = string.Empty,
                ["Gemini:ApiKey"] = string.Empty,
                ["PublicUrl:BaseUrl"] = ConfiguredPublicBaseUrl,
            });
            config.AddInMemoryCollection(TestAuthentication.ConfigOverrides);
        });

        // Program.cs já registrou AppDbContext (AddInfrastructure) apontando para a
        // connection string do appsettings.Development.json. Substituímos aqui, depois
        // que os serviços da aplicação já foram registrados, para garantir que os testes
        // sempre usem o Postgres efêmero do Testcontainers, nunca um Postgres real do
        // ambiente de desenvolvimento.
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>(options => options.UseButecoAgentsNpgsql(_postgres.GetConnectionString()));

            // O fixture NÃO sobe RabbitMQ, e a partir da change
            // knowledge-base-indexacao todo POST/PUT de documento publica na
            // fila de indexação. Sem esta substituição o publisher real tenta
            // abrir conexão e derruba todo teste que cria documento — inclusive
            // os 12 que existiam antes e não têm relação com indexação.
            var indexingPublisher = services.SingleOrDefault(
                d => d.ServiceType == typeof(IKnowledgeIndexingJobPublisher));
            if (indexingPublisher is not null)
            {
                services.Remove(indexingPublisher);
            }

            services.AddSingleton<IKnowledgeIndexingJobPublisher>(IndexingPublisher);
        });

        // Sink do SQL emitido pelo EF Core. Inerte para quem não usa
        // SqlCapture: só enfileira strings, e a categoria de comando do EF já é
        // logada em Information por padrão.
        builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(SqlCapture);
            logging.AddFilter(EmittedSqlCapture.EfCommandCategory, LogLevel.Information);
        });
    }

    /// <summary>
    /// Captura do SQL de produção, usada pela metade determinística dos guardas
    /// de ordenação (ver <see cref="EmittedSqlCapture"/>).
    /// </summary>
    public EmittedSqlCapture SqlCapture { get; } = new();

    /// <summary>
    /// Registra o que foi publicado na fila de indexação. É o que permite
    /// afirmar tanto "enfileirou" quanto "NÃO enfileirou" — o par da regra de
    /// <c>ContentHash</c>.
    /// </summary>
    public FakeKnowledgeIndexingJobPublisher IndexingPublisher { get; } = new();

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        TestAuthentication.AttachOperatorToken(client, Services);
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }
}
