using System.Text.Json;
using global::A2A;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using ApiAgent = Buteco.Api.Agents.Entities.Agent;
using ApiDbContext = Buteco.Api.Infrastructure.AppDbContext;
using ApiTaskStore = Buteco.Api.A2A.PostgresTaskStore;
using Buteco.Workers.Agents;
using TaskStatus = A2A.TaskStatus;
using WorkerDbContext = Buteco.Workers.Infrastructure.AppDbContext;
using WorkerTaskStore = Buteco.Workers.A2A.PostgresTaskStore;
using CrossAppTaskStoreCompatibility.Tests.Support;

namespace CrossAppTaskStoreCompatibility.Tests;

/// <summary>
/// Grava uma task via <c>PostgresTaskStore</c> de um app e lê de volta via o
/// do outro (e vice-versa) contra o mesmo Postgres, verificando
/// automaticamente que as duas implementações independentes concordam no
/// schema (ver Decisão 8 em design.md).
/// </summary>
public class PostgresTaskStoreCompatibilityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .WithDatabase("buteco_cross_app_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    private IServiceScopeFactory _apiScopeFactory = null!;
    private IServiceScopeFactory _workerScopeFactory = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var apiServices = new ServiceCollection();
        apiServices.AddDbContext<ApiDbContext>(options => options.UseButecoAgentsNpgsql(_postgres.GetConnectionString()));
        _apiScopeFactory = apiServices.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var workerServices = new ServiceCollection();
        workerServices.AddDbContext<WorkerDbContext>(options => options.UseButecoAgentsNpgsql(_postgres.GetConnectionString()));
        _workerScopeFactory = workerServices.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        // Só apps/api aplica migration em runtime (é quem cria o agente antes
        // de qualquer task existir) — ver Decisão 2/4 em design.md.
        using var scope = _apiScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task TaskWrittenByApiStore_IsReadCorrectlyByWorkersStore()
    {
        var agentId = await SeedAgentAsync();
        var apiStore = new ApiTaskStore(_apiScopeFactory, agentId);
        var workerStore = new WorkerTaskStore(_workerScopeFactory, agentId);

        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");
        var originalTask = BuildSampleTask(taskId, contextId, "Escrito pela API");

        await apiStore.SaveTaskAsync(taskId, originalTask);

        var readByWorker = await workerStore.GetTaskAsync(taskId);

        AssertTasksMatch(originalTask, readByWorker);
    }

    [Fact]
    public async Task TaskWrittenByWorkersStore_IsReadCorrectlyByApiStore()
    {
        var agentId = await SeedAgentAsync();
        var apiStore = new ApiTaskStore(_apiScopeFactory, agentId);
        var workerStore = new WorkerTaskStore(_workerScopeFactory, agentId);

        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");
        var originalTask = BuildSampleTask(taskId, contextId, "Escrito pelo Worker");

        await workerStore.SaveTaskAsync(taskId, originalTask);

        var readByApi = await apiStore.GetTaskAsync(taskId);

        AssertTasksMatch(originalTask, readByApi);
    }

    private async Task<Guid> SeedAgentAsync()
    {
        using var scope = _apiScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApiDbContext>();

        var agent = new ApiAgent("Agente de teste cruzado", "Instruções de teste.", "openai", "gpt-5.6-sol", null, []);
        dbContext.Agents.Add(agent);
        await dbContext.SaveChangesAsync();

        return agent.Id;
    }

    // Passa pelo mesmo ConversationSessionCodec.Encode que apps/workers usa
    // de verdade (design.md da change apps-workers-historico-conversa,
    // Decisão 10) — não um JsonElement de estrutura arbitrária. O valor real
    // guardado em Metadata é uma string JSON escapada (escalar, opaca para o
    // jsonb do Postgres); testar com um objeto aninhado cru não pegaria a
    // reordenação de propriedades que o jsonb faz e que motivou o codec.
    private static JsonElement BuildSampleConversationSession()
    {
        using var document = JsonDocument.Parse(
            """{"messages":[{"role":"user","text":"Pergunta original"}]}""");
        return ConversationSessionCodec.Encode(document.RootElement.Clone());
    }

    private static AgentTask BuildSampleTask(string taskId, string contextId, string artifactText) => new()
    {
        Id = taskId,
        ContextId = contextId,
        Status = new TaskStatus { State = TaskState.Completed, Timestamp = DateTimeOffset.UtcNow },
        History =
        [
            new Message
            {
                Role = Role.User,
                Parts = [Part.FromText("Pergunta original")],
                MessageId = Guid.NewGuid().ToString("N"),
                ContextId = contextId,
            },
        ],
        Artifacts =
        [
            new Artifact
            {
                ArtifactId = Guid.NewGuid().ToString("N"),
                Parts = [Part.FromText(artifactText)],
            },
        ],
        Metadata = new Dictionary<string, JsonElement>
        {
            ["conversationSession"] = BuildSampleConversationSession(),
        },
    };

    private static void AssertTasksMatch(AgentTask expected, AgentTask? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Id, actual!.Id);
        Assert.Equal(expected.ContextId, actual.ContextId);
        Assert.Equal(expected.Status.State, actual.Status.State);
        Assert.Equal(expected.History?.Count ?? 0, actual.History?.Count ?? 0);
        Assert.Equal(expected.Artifacts?.Count ?? 0, actual.Artifacts?.Count ?? 0);
        Assert.Equal(
            expected.Artifacts?.SelectMany(a => a.Parts).Select(p => p.Text).ToList(),
            actual.Artifacts?.SelectMany(a => a.Parts).Select(p => p.Text).ToList());

        Assert.NotNull(actual.Metadata);
        Assert.True(actual.Metadata!.ContainsKey("conversationSession"));

        // Comparação de texto bruto é deliberada, não um descuido — é o que
        // detecta os dois PostgresTaskStore divergindo em
        // JsonSerializerOptions (ex.: encoder de escaping) entre si, mesmo
        // quando os dois JSON decodificam para o mesmo valor. Já pegou um
        // bug de produção real (ver design.md da change
        // crossapp-session-codec-encoder). Não trocar por comparação
        // semântica achando que é conserto de teste: isso mataria a
        // capacidade desta asserção de guardar esse acordo.
        Assert.Equal(
            expected.Metadata!["conversationSession"].GetRawText(),
            actual.Metadata["conversationSession"].GetRawText());
    }
}
