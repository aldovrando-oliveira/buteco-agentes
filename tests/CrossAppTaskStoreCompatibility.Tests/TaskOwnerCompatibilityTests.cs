using global::A2A;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using ApiAgent = Buteco.Api.Agents.Entities.Agent;
using ApiDbContext = Buteco.Api.Infrastructure.AppDbContext;
using ApiTaskStore = Buteco.Api.A2A.PostgresTaskStore;
using ExternalApplication = Buteco.Api.ExternalApplications.ExternalApplication;
using TaskOwnerScope = Buteco.Api.A2A.TaskOwnerScope;
using TaskStatus = A2A.TaskStatus;
using WorkerDbContext = Buteco.Workers.Infrastructure.AppDbContext;
using WorkerTaskStore = Buteco.Workers.A2A.PostgresTaskStore;
using CrossAppTaskStoreCompatibility.Tests.Support;

namespace CrossAppTaskStoreCompatibility.Tests;

/// <summary>
/// O dono da task entre o <c>apps/api</c> e o <c>apps/workers</c> (change agentes-por-aplicacao, D3 e D4;
/// seção 5 do tasks.md). Só o <c>apps/api</c> mapeia <c>a2a_tasks.application_id</c>; o
/// <c>apps/workers</c> grava as transições e cria a task filha da delegação sem conhecer a coluna. Os dois
/// stores são os de produção, contra o mesmo Postgres migrado só pelo <c>apps/api</c>, como em produção.
/// </summary>
public class TaskOwnerCompatibilityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .WithDatabase("buteco_cross_app_owner_test")
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

        using var scope = _apiScopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApiDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // (guarda 5.1) "O worker não apaga o dono" (a2a-task-lifecycle). O caminho de produção: o apps/api cria
    // a task com a aplicação como dona e a regrava com a mensagem (o SDK faz isso no SendMessage); o
    // apps/workers a leva a working e a completed. A linha continua com o mesmo dono. Defeito: o Update do
    // A2ATaskRecord do apps/api zerando o ApplicationId.
    [Fact]
    public async Task TheOwner_SurvivesTheTransitions_OfBothApps()
    {
        var agentId = await SeedAgentAsync();
        var applicationId = await SeedApplicationAsync();
        var apiStore = new ApiTaskStore(_apiScopeFactory, agentId);
        var workerStore = new WorkerTaskStore(_workerScopeFactory, agentId);
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");

        using (TaskOwnerScope.Begin(applicationId))
        {
            await apiStore.SaveTaskAsync(taskId, BuildTask(taskId, contextId, TaskState.Submitted));
            await apiStore.SaveTaskAsync(taskId, BuildTask(taskId, contextId, TaskState.Submitted, withMessage: true));
        }

        await workerStore.SaveTaskAsync(taskId, BuildTask(taskId, contextId, TaskState.Working, withMessage: true));
        await workerStore.SaveTaskAsync(taskId, BuildTask(taskId, contextId, TaskState.Completed, withMessage: true));

        Assert.Equal(applicationId, await OwnerAsync(taskId));
        Assert.Equal(TaskState.Completed, (await apiStore.GetTaskAsync(taskId))!.Status.State);
    }

    // "Filha num agente não liberado" (a2a-task-lifecycle), a parte do acordo: a task que o apps/workers cria,
    // como a delegação faz (AgentDelegationToolSetResolver), nasce sem dono, mesmo com a mãe de uma
    // aplicação. A resposta da rota para ela é o caso do apps/api (5.2 do tasks.md).
    [Fact]
    public async Task ATaskCreatedByTheWorkers_HasNoOwner_EvenWhenTheParentHasOne()
    {
        var parentAgent = await SeedAgentAsync();
        var childAgent = await SeedAgentAsync();
        var applicationId = await SeedApplicationAsync();
        var parentId = Guid.NewGuid().ToString("N");
        var childId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");
        using (TaskOwnerScope.Begin(applicationId))
        {
            await new ApiTaskStore(_apiScopeFactory, parentAgent).SaveTaskAsync(parentId, BuildTask(parentId, contextId, TaskState.Working));
        }

        await new WorkerTaskStore(_workerScopeFactory, childAgent).SaveTaskAsync(childId, BuildTask(childId, contextId, TaskState.Submitted, withMessage: true));

        Assert.Equal(applicationId, await OwnerAsync(parentId));
        Assert.Null(await OwnerAsync(childId));
    }

    private async Task<Guid> SeedAgentAsync()
    {
        using var scope = _apiScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var agent = new ApiAgent("Agente do dono", "Instruções de teste.", "openai", "gpt-5.6-sol", null, []);
        dbContext.Agents.Add(agent);
        await dbContext.SaveChangesAsync();
        return agent.Id;
    }

    private async Task<Guid> SeedApplicationAsync()
    {
        using var scope = _apiScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var application = ExternalApplication.Create($"btc_app_{suffix}", $"Aplicação {suffix}", null, DateTimeOffset.UtcNow);
        dbContext.ExternalApplications.Add(application);
        await dbContext.SaveChangesAsync();
        return application.Id;
    }

    private async Task<Guid?> OwnerAsync(string taskId)
    {
        using var scope = _apiScopeFactory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApiDbContext>().A2ATasks
            .Where(task => task.TaskId == taskId).Select(task => task.ApplicationId).SingleAsync();
    }

    private static AgentTask BuildTask(string taskId, string contextId, TaskState state, bool withMessage = false) => new()
    {
        Id = taskId,
        ContextId = contextId,
        Status = new TaskStatus { State = state, Timestamp = DateTimeOffset.UtcNow },
        History = withMessage
            ?
            [
                new Message
                {
                    Role = Role.User,
                    Parts = [Part.FromText("Olá")],
                    MessageId = Guid.NewGuid().ToString("N"),
                    ContextId = contextId,
                },
            ]
            : null,
    };
}
