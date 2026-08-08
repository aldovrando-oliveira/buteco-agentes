using System.Text.Json;
using global::A2A;
using Buteco.Workers.A2A;
using Buteco.Workers.AgentDelegations;
using Buteco.Workers.Agents;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Mcp;
using Buteco.Workers.Messaging;
using Buteco.Workers.Options;
using Buteco.Workers.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using TaskStatus = A2A.TaskStatus;

namespace Buteco.Workers.Tests;

public class TaskJobConsumerTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    [Fact]
    public async Task Consumer_ProcessesJob_TaskEndsCompletedWithArtifact()
    {
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAndTaskAsync(agentId, "Atendente", "Responda com simpatia.", taskId, contextId, "Qual é a capital da França?");

        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Paris")));

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            await PublishJobAsync(taskId, agentId, contextId);

            var record = await PollUntilTerminalAsync(taskId);

            Assert.Equal(nameof(TaskState.Completed), record.State);
            var task = JsonSerializer.Deserialize<AgentTask>(record.Payload, A2AJsonUtilities.DefaultOptions)!;
            Assert.NotNull(task.Artifacts);
            Assert.Contains(task.Artifacts!, artifact => artifact.Parts.Any(part => part.Text == "Paris"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Consumer_WhenChatClientFails_TaskEndsFailed()
    {
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAndTaskAsync(agentId, "Atendente", "Responda com simpatia.", taskId, contextId, "oi");

        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM indisponível"));

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            await PublishJobAsync(taskId, agentId, contextId);

            var record = await PollUntilTerminalAsync(taskId);

            Assert.Equal(nameof(TaskState.Failed), record.State);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Consumer_WhenAgentProviderNotConfiguredInWorkerEnvironment_TaskEndsFailed()
    {
        // Simula divergência de configuração entre apps/api e apps/workers
        // (ver design.md, Decision 5, camada complementar): o agente foi
        // cadastrado com um provider que apps/api considerou configurado,
        // mas o ambiente de apps/workers não tem a chave desse provider.
        // Usa o ChatClientResolver real (não mockado) para provar que ele
        // lança e o catch (Exception) já existente em
        // AgentExecutionService.ExecuteAsync trata o caso, sem derrubar o worker.
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAndTaskAsync(agentId, "Atendente", "Responda com simpatia.", taskId, contextId, "oi", provider: "anthropic", model: "claude-opus-5");

        using var host = BuildHostWithRealResolver();
        await host.StartAsync();

        try
        {
            await PublishJobAsync(taskId, agentId, contextId);

            var record = await PollUntilTerminalAsync(taskId);

            Assert.Equal(nameof(TaskState.Failed), record.State);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Consumer_WhenTaskRowNotYetCommittedAtConsumeTime_RetriesThenProcessesSuccessfully()
    {
        // Reproduz a corrida real entre A2AServer publicando no RabbitMQ e o
        // SaveTaskAsync do evento "submitted" ainda não ter commitado quando
        // o worker consome a mensagem — descoberta via teste manual, ver
        // design.md da change apps-workers-historico-conversa, Decisão 9.
        // Aqui simulamos publicando o job ANTES da task existir no store, e
        // só inserindo a task (com atraso) depois.
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(agentId, "Atendente", "Responda com simpatia.");

        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Paris")));

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            await PublishJobAsync(taskId, agentId, contextId);

            // Atraso maior que uma tentativa de retry (100ms cada), menor que
            // o total (5 tentativas) — garante que o worker precisou
            // realmente tentar de novo, não passou de primeira por sorte.
            await Task.Delay(250);
            await SeedTaskAsync(taskId, agentId, contextId, "Qual é a capital da França?");

            var record = await PollUntilTerminalAsync(taskId);

            Assert.Equal(nameof(TaskState.Completed), record.State);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Consumer_WhenTaskExistsButUserMessageNotYetPersisted_RetriesUntilMessageIsPresent()
    {
        // Segunda camada da mesma corrida (design.md, Decisão 9): o A2AServer
        // grava a task em DOIS eventos separados — SubmitAsync() cria a
        // linha (History nulo) e só depois, após uma leitura de estado do
        // agente, EnqueueMessageAsync grava a mensagem do usuário nela —
        // enquanto publica no RabbitMQ concorrentemente. Um worker pode
        // consumir a mensagem e achar a task já criada, mas ainda sem a
        // mensagem do usuário. Aqui simulamos publicando o job com a task já
        // existente porém sem History, adicionando a mensagem só depois de
        // um atraso.
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(agentId, "Atendente", "Responda com simpatia.");
        await SeedTaskWithoutUserMessageAsync(taskId, agentId, contextId);

        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Paris")));

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            await PublishJobAsync(taskId, agentId, contextId);

            // Atraso maior que uma tentativa de retry (100ms cada), menor que
            // o total (5 tentativas) — garante que o worker precisou
            // realmente tentar de novo, não passou de primeira por sorte.
            await Task.Delay(250);
            await AddUserMessageToTaskAsync(taskId, contextId, "Qual é a capital da França?");

            var record = await PollUntilTerminalAsync(taskId);

            Assert.Equal(nameof(TaskState.Completed), record.State);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private IHost BuildHostWithRealResolver()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration["ConnectionStrings:Postgres"] = fixture.Postgres.GetConnectionString();
        builder.Services.AddInfrastructure(builder.Configuration);

        builder.Services.Configure<RabbitMqOptions>(options =>
        {
            options.Host = fixture.RabbitMq.Hostname;
            options.Port = fixture.RabbitMq.GetMappedPublicPort(5672);
            options.Username = "buteco";
            options.Password = "buteco_test_password";
        });

        // AnthropicOptions/GeminiOptions nunca configurados aqui de propósito —
        // é exatamente o ambiente "chave ausente" que este teste exercita.
        builder.Services.AddSingleton<IChatClientResolver, ChatClientResolver>();
        builder.Services.AddSingleton<IMcpToolSetResolver, NullMcpToolSetResolver>();
        builder.Services.AddSingleton<IAgentDelegationToolSetResolver, NullAgentDelegationToolSetResolver>();
        builder.Services.AddSingleton<AgentExecutionService>();
        builder.Services.AddHostedService<TaskJobConsumer>();

        return builder.Build();
    }

    private IHost BuildHost(IChatClient chatClient)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration["ConnectionStrings:Postgres"] = fixture.Postgres.GetConnectionString();
        builder.Services.AddInfrastructure(builder.Configuration);

        builder.Services.Configure<RabbitMqOptions>(options =>
        {
            options.Host = fixture.RabbitMq.Hostname;
            options.Port = fixture.RabbitMq.GetMappedPublicPort(5672);
            options.Username = "buteco";
            options.Password = "buteco_test_password";
        });

        var resolverMock = new Mock<IChatClientResolver>();
        resolverMock.Setup(resolver => resolver.Resolve(It.IsAny<string>(), It.IsAny<string>())).Returns(chatClient);
        builder.Services.AddSingleton(resolverMock.Object);
        builder.Services.AddSingleton<IMcpToolSetResolver, NullMcpToolSetResolver>();
        builder.Services.AddSingleton<IAgentDelegationToolSetResolver, NullAgentDelegationToolSetResolver>();
        builder.Services.AddSingleton<AgentExecutionService>();
        builder.Services.AddHostedService<TaskJobConsumer>();

        return builder.Build();
    }

    private async Task SeedAgentAndTaskAsync(
        Guid agentId,
        string agentName,
        string instructions,
        string taskId,
        string contextId,
        string userMessage,
        string provider = "openai",
        string model = "gpt-5.6-sol")
    {
        await SeedAgentAsync(agentId, agentName, instructions, provider, model);
        await SeedTaskAsync(taskId, agentId, contextId, userMessage);
    }

    private async Task SeedAgentAsync(
        Guid agentId, string agentName, string instructions, string provider = "openai", string model = "gpt-5.6-sol")
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Postgres.GetConnectionString()).Options;
        await using var dbContext = new AppDbContext(options);

        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "Provider", "Model", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {agentName}, {instructions}, {provider}, {model}, {now}, {now})
             """);
    }

    private async Task SeedTaskAsync(string taskId, Guid agentId, string contextId, string userMessage)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Postgres.GetConnectionString()).Options;
        await using var dbContext = new AppDbContext(options);

        var now = DateTimeOffset.UtcNow;
        var agentTask = new AgentTask
        {
            Id = taskId,
            ContextId = contextId,
            Status = new TaskStatus { State = TaskState.Submitted, Timestamp = now },
            History =
            [
                new Message
                {
                    Role = Role.User,
                    Parts = [Part.FromText(userMessage)],
                    MessageId = Guid.NewGuid().ToString("N"),
                    ContextId = contextId,
                },
            ],
        };

        var payload = JsonSerializer.Serialize(agentTask, A2AJsonUtilities.DefaultOptions);
        dbContext.A2ATasks.Add(new A2ATaskRecord(taskId, agentId, contextId, nameof(TaskState.Submitted), now, payload));
        await dbContext.SaveChangesAsync();
    }

    private async Task SeedTaskWithoutUserMessageAsync(string taskId, Guid agentId, string contextId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Postgres.GetConnectionString()).Options;
        await using var dbContext = new AppDbContext(options);

        var now = DateTimeOffset.UtcNow;
        var agentTask = new AgentTask
        {
            Id = taskId,
            ContextId = contextId,
            Status = new TaskStatus { State = TaskState.Submitted, Timestamp = now },
        };

        var payload = JsonSerializer.Serialize(agentTask, A2AJsonUtilities.DefaultOptions);
        dbContext.A2ATasks.Add(new A2ATaskRecord(taskId, agentId, contextId, nameof(TaskState.Submitted), now, payload));
        await dbContext.SaveChangesAsync();
    }

    private async Task AddUserMessageToTaskAsync(string taskId, string contextId, string userMessage)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Postgres.GetConnectionString()).Options;
        await using var dbContext = new AppDbContext(options);

        var record = await dbContext.A2ATasks.FirstAsync(t => t.TaskId == taskId);
        var task = JsonSerializer.Deserialize<AgentTask>(record.Payload, A2AJsonUtilities.DefaultOptions)!;
        task.History =
        [
            new Message
            {
                Role = Role.User,
                Parts = [Part.FromText(userMessage)],
                MessageId = Guid.NewGuid().ToString("N"),
                ContextId = contextId,
            },
        ];

        var payload = JsonSerializer.Serialize(task, A2AJsonUtilities.DefaultOptions);
        record.Update(task.ContextId, task.Status.State.ToString(), task.Status.Timestamp, payload);
        await dbContext.SaveChangesAsync();
    }

    private async Task PublishJobAsync(string taskId, Guid agentId, string contextId)
    {
        var factory = new ConnectionFactory
        {
            HostName = fixture.RabbitMq.Hostname,
            Port = fixture.RabbitMq.GetMappedPublicPort(5672),
            UserName = "buteco",
            Password = "buteco_test_password",
        };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(TaskJobConsumer.QueueName, durable: true, exclusive: false, autoDelete: false);

        var message = new TaskJobMessage(taskId, agentId, contextId);
        var body = JsonSerializer.SerializeToUtf8Bytes(message);

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: TaskJobConsumer.QueueName,
            mandatory: true,
            basicProperties: new BasicProperties { Persistent = true },
            body: body);
    }

    private async Task<A2ATaskRecord> PollUntilTerminalAsync(string taskId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Postgres.GetConnectionString()).Options;

        for (var attempt = 0; attempt < 50; attempt++)
        {
            await using var dbContext = new AppDbContext(options);
            var record = await dbContext.A2ATasks.AsNoTracking().FirstOrDefaultAsync(t => t.TaskId == taskId);

            if (record is not null && record.State is nameof(TaskState.Completed) or nameof(TaskState.Failed))
            {
                return record;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Task '{taskId}' não atingiu um estado terminal a tempo.");
    }
}
