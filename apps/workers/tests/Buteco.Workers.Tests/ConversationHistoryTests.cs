using System.Text.Json;
using global::A2A;
using Buteco.Workers.A2A;
using Buteco.Workers.Agents;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Messaging;
using Buteco.Workers.Options;
using Buteco.Workers.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using RabbitMQ.Client;
using TaskStatus = A2A.TaskStatus;

namespace Buteco.Workers.Tests;

/// <summary>
/// Cobre a change apps-workers-historico-conversa: histórico de conversa
/// propagado entre tasks do mesmo contextId (ver design.md e
/// specs/a2a-task-lifecycle/spec.md daquela change).
/// </summary>
public class ConversationHistoryTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    // Deve bater com AgentExecutionService.MaxHistoryMessages (privado de
    // propósito — não vale a pena expor via InternalsVisibleTo só para
    // este teste). Se o valor lá mudar, este teste precisa acompanhar.
    // Revisado de 20 para 200 pela change apps-workers-resumo-historico-conversa
    // (design.md, Decisão 9) — o mecanismo de truncamento em si não mudou,
    // só o valor do teto, então este teste continua válido, só mais lento.
    private const int MaxHistoryMessages = 200;

    [Fact]
    public async Task SecondMessageInSameContext_ProcessedByDifferentWorkerInstance_IncludesFirstTurnHistory()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var firstTaskId = Guid.NewGuid().ToString("N");
        var secondTaskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(agentId, "Atendente", "Responda com simpatia.");
        await SeedTaskAsync(firstTaskId, agentId, contextId, "Qual é a capital da França?");

        var firstChatClient = new Mock<IChatClient>();
        firstChatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Paris")));

        // Instância A: processa a primeira mensagem e é totalmente
        // descartada (host parado e disposed) antes da instância B sequer
        // existir — nenhum objeto é compartilhado entre elas além do
        // Postgres/RabbitMQ do fixture, provando que o mecanismo não
        // depende de estado em memória de uma instância específica.
        using (var hostA = BuildHost(firstChatClient.Object))
        {
            await hostA.StartAsync();
            await PublishJobAsync(firstTaskId, agentId, contextId);
            var firstRecord = await PollUntilTerminalAsync(firstTaskId);
            Assert.Equal(nameof(TaskState.Completed), firstRecord.State);
            await hostA.StopAsync();
        }

        await SeedTaskAsync(secondTaskId, agentId, contextId, "E a população da cidade?");

        List<ChatMessage>? capturedMessages = null;
        var secondChatClient = new Mock<IChatClient>();
        secondChatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((messages, _, _) => capturedMessages = messages.ToList())
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Cerca de 2 milhões")));

        // Instância B: construída do zero (próprio IHost/DI container),
        // nunca teve acesso a nenhum objeto da instância A.
        using (var hostB = BuildHost(secondChatClient.Object))
        {
            await hostB.StartAsync();
            await PublishJobAsync(secondTaskId, agentId, contextId);
            var secondRecord = await PollUntilTerminalAsync(secondTaskId);
            Assert.Equal(nameof(TaskState.Completed), secondRecord.State);
            await hostB.StopAsync();
        }

        Assert.NotNull(capturedMessages);
        Assert.Contains(capturedMessages!, m => m.Text == "Qual é a capital da França?");
        Assert.Contains(capturedMessages!, m => m.Text == "Paris");
        Assert.Contains(capturedMessages!, m => m.Text == "E a população da cidade?");
    }

    [Fact]
    public async Task HistoryExceedingLimit_OnlyMostRecentMessagesReachTheModel()
    {
        var turnsToExceedLimit = (MaxHistoryMessages / 2) + 2; // mensagens (user+assistant) > MaxHistoryMessages

        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(agentId, "Atendente", "Responda com simpatia.");

        List<ChatMessage>? lastCapturedMessages = null;
        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((messages, _, _) => lastCapturedMessages = messages.ToList())
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            for (var turn = 1; turn <= turnsToExceedLimit; turn++)
            {
                var taskId = Guid.NewGuid().ToString("N");
                await SeedTaskAsync(taskId, agentId, contextId, $"pergunta {turn}");
                await PublishJobAsync(taskId, agentId, contextId);

                var record = await PollUntilTerminalAsync(taskId);
                Assert.Equal(nameof(TaskState.Completed), record.State);
            }
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.NotNull(lastCapturedMessages);
        Assert.DoesNotContain(lastCapturedMessages!, m => m.Text == "pergunta 1");
        Assert.Contains(lastCapturedMessages!, m => m.Text == $"pergunta {turnsToExceedLimit}");
    }

    [Fact]
    public async Task PreviousFailedTask_DoesNotAppearInNextTaskHistory()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var failingTaskId = Guid.NewGuid().ToString("N");
        var nextTaskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(agentId, "Atendente", "Responda com simpatia.");
        await SeedTaskAsync(failingTaskId, agentId, contextId, "pergunta que falha");

        var failingChatClient = new Mock<IChatClient>();
        failingChatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM indisponível"));

        using (var failingHost = BuildHost(failingChatClient.Object))
        {
            await failingHost.StartAsync();
            await PublishJobAsync(failingTaskId, agentId, contextId);
            var failedRecord = await PollUntilTerminalAsync(failingTaskId);
            Assert.Equal(nameof(TaskState.Failed), failedRecord.State);
            await failingHost.StopAsync();
        }

        await SeedTaskAsync(nextTaskId, agentId, contextId, "pergunta seguinte");

        List<ChatMessage>? capturedMessages = null;
        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((messages, _, _) => capturedMessages = messages.ToList())
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        using (var host = BuildHost(chatClient.Object))
        {
            await host.StartAsync();
            await PublishJobAsync(nextTaskId, agentId, contextId);
            var nextRecord = await PollUntilTerminalAsync(nextTaskId);
            Assert.Equal(nameof(TaskState.Completed), nextRecord.State);
            await host.StopAsync();
        }

        Assert.NotNull(capturedMessages);
        Assert.DoesNotContain(capturedMessages!, m => m.Text == "pergunta que falha");
        Assert.Contains(capturedMessages!, m => m.Text == "pergunta seguinte");
    }

    [Fact]
    public async Task AgentInstructionsUpdatedBetweenMessages_SecondCallUsesNewInstructions()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var firstTaskId = Guid.NewGuid().ToString("N");
        var secondTaskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(agentId, "Atendente", "Instruções antigas.");
        await SeedTaskAsync(firstTaskId, agentId, contextId, "primeira mensagem");

        var firstChatClient = new Mock<IChatClient>();
        firstChatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        using (var hostA = BuildHost(firstChatClient.Object))
        {
            await hostA.StartAsync();
            await PublishJobAsync(firstTaskId, agentId, contextId);
            var firstRecord = await PollUntilTerminalAsync(firstTaskId);
            Assert.Equal(nameof(TaskState.Completed), firstRecord.State);
            await hostA.StopAsync();
        }

        // Simula PUT /agents/{id} no meio da conversa em andamento.
        await UpdateAgentInstructionsAsync(agentId, "Instruções novas.");
        await SeedTaskAsync(secondTaskId, agentId, contextId, "segunda mensagem");

        ChatOptions? capturedOptions = null;
        var secondChatClient = new Mock<IChatClient>();
        secondChatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((_, options, _) => capturedOptions = options)
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        using (var hostB = BuildHost(secondChatClient.Object))
        {
            await hostB.StartAsync();
            await PublishJobAsync(secondTaskId, agentId, contextId);
            var secondRecord = await PollUntilTerminalAsync(secondTaskId);
            Assert.Equal(nameof(TaskState.Completed), secondRecord.State);
            await hostB.StopAsync();
        }

        Assert.NotNull(capturedOptions);
        Assert.Equal("Instruções novas.", capturedOptions!.Instructions);
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
        builder.Services.AddSingleton<AgentExecutionService>();
        builder.Services.AddHostedService<TaskJobConsumer>();

        return builder.Build();
    }

    private async Task SeedAgentAsync(Guid agentId, string agentName, string instructions, string provider = "openai", string model = "gpt-5.6-sol")
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

    private async Task UpdateAgentInstructionsAsync(Guid agentId, string instructions)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Postgres.GetConnectionString()).Options;
        await using var dbContext = new AppDbContext(options);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE agents SET "Instructions" = {instructions}, "UpdatedAt" = {DateTimeOffset.UtcNow}
             WHERE "Id" = {agentId}
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
