using System.Text.Json;
using global::A2A;
using Buteco.Workers.A2A;
using Buteco.Workers.Agents;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Mcp;
using Buteco.Workers.Mcp.Security;
using Buteco.Workers.Messaging;
using Buteco.Workers.Options;
using Buteco.Workers.Tests.Mcp.Support;
using Buteco.Workers.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using RabbitMQ.Client;
using TaskStatus = A2A.TaskStatus;

namespace Buteco.Workers.Tests.Mcp;

/// <summary>
/// Cobre a change apps-workers-execucao-mcp fim a fim: pipeline real
/// (RabbitMQ + Postgres + <see cref="TaskJobConsumer"/> +
/// <see cref="AgentExecutionService"/>) com uma tool MCP real (via
/// <see cref="FakeMcpServerHttpMessageHandler"/>) sendo de fato chamada pelo
/// <c>FunctionInvokingChatClient</c> que o <c>ChatClientAgent</c> insere
/// automaticamente (design.md, Decision 1). Mesmo estilo de
/// <c>HistorySummarizationTests</c>.
/// </summary>
public class McpToolExecutionEndToEndTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    private const string EncryptionKey = "NtxqjqnKG3sqy52PFRh/SGk573bsE9TrDtKOsDiR8uc=";

    [Fact]
    public async Task RoundTrip_ToolCallDecidedByLlm_ReachesRealMcpServer_AndAffectsFinalTaskResult()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var serverUrl = UniqueServerUrl();
        var handler = new FakeMcpServerHttpMessageHandler();
        handler.ConfigureServer(serverUrl, new FakeMcpServerConfig
        {
            AvailableTools = [new FakeMcpTool("search")],
            ToolCallResult = _ => "42 clientes ativos",
        });

        await SeedAgentAsync(agentId);
        var mcpServerId = await SeedMcpServerAsync("CRM", serverUrl);
        await SeedAgentMcpServerAsync(agentId, mcpServerId, ["search"]);

        var chatClient = BuildToolCallingChatClientMock("CRM__search", toolCallCount: 1, out var finalResponseTextCapture);

        using var host = BuildHost(chatClient.Object, handler);
        await host.StartAsync();

        A2ATaskRecord record;
        try
        {
            record = await RunTurnAsync(agentId, contextId, "Quantos clientes ativos temos?");
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.Equal(nameof(TaskState.Completed), record.State);
        Assert.NotNull(finalResponseTextCapture());
        Assert.Contains("42 clientes ativos", finalResponseTextCapture());
        Assert.Equal(1, handler.CountRequests(serverUrl, "tools/call"));
    }

    [Fact]
    public async Task SameToolCalledTwiceInSameTurn_RoutesBothCalls_WithoutReconnecting()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var serverUrl = UniqueServerUrl();
        var handler = new FakeMcpServerHttpMessageHandler();
        handler.ConfigureServer(serverUrl, new FakeMcpServerConfig
        {
            AvailableTools = [new FakeMcpTool("search")],
            ToolCallResult = _ => "ok",
        });

        await SeedAgentAsync(agentId);
        var mcpServerId = await SeedMcpServerAsync("CRM", serverUrl);
        await SeedAgentMcpServerAsync(agentId, mcpServerId, ["search"]);

        // LLM fake decide chamar a mesma tool duas vezes antes de responder
        // com texto final — exercita o loop de tool-calling do
        // FunctionInvokingChatClient chamando a MESMA AITool (McpClientTool)
        // mais de uma vez dentro da mesma execução de RunAsync.
        var chatClient = BuildToolCallingChatClientMock("CRM__search", toolCallCount: 2, out _);

        using var host = BuildHost(chatClient.Object, handler);
        await host.StartAsync();

        A2ATaskRecord record;
        try
        {
            record = await RunTurnAsync(agentId, contextId, "Busque duas vezes, por favor.");
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.Equal(nameof(TaskState.Completed), record.State);

        // As duas chamadas de tool foram roteadas com sucesso...
        Assert.Equal(2, handler.CountRequests(serverUrl, "tools/call"));

        // ...mas só houve UM handshake initialize — a conexão MCP aberta na
        // resolução (antes de RunAsync) foi reaproveitada para as duas
        // chamadas, não reaberta a cada uma (design.md, Decision 4).
        Assert.Equal(1, handler.CountRequests(serverUrl, "initialize"));
    }

    [Fact]
    public async Task ToolCallKeepsFailing_TaskEndsAsFailed_WithoutCrashingWorker()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var serverUrl = UniqueServerUrl();
        var handler = new FakeMcpServerHttpMessageHandler();
        handler.ConfigureServer(serverUrl, new FakeMcpServerConfig
        {
            AvailableTools = [new FakeMcpTool("search")],
            FailToolCalls = true,
        });

        await SeedAgentAsync(agentId);
        var mcpServerId = await SeedMcpServerAsync("CRM", serverUrl);
        await SeedAgentMcpServerAsync(agentId, mcpServerId, ["search"]);

        // LLM fake sempre pede a mesma tool, que sempre falha no servidor —
        // FunctionInvokingChatClient tenta algumas vezes antes de desistir
        // (MaximumConsecutiveErrorsPerRequest, default 3) e propagar a
        // exceção pra fora de RunAsync (task 4.3: confirma que o catch
        // (Exception) já existente de AgentExecutionService.ExecuteAsync
        // cobre esse caso, sem tratamento novo). callId precisa ser novo a
        // cada iteração — um callId repetido é tratado pelo
        // FunctionInvokingChatClient como já resolvido (não reinvoca a
        // tool), o que mascararia o cenário de falha repetida.
        var chatClient = new Mock<IChatClient>();
        var callIndex = 0;
        chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.Is<ChatOptions?>(o => o != null),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callIndex++;
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    new List<AIContent> { new FunctionCallContent($"call-{callIndex}", "CRM__search", new Dictionary<string, object?>()) })));
            });

        using var host = BuildHost(chatClient.Object, handler);
        await host.StartAsync();

        A2ATaskRecord record;
        try
        {
            record = await RunTurnAsync(agentId, contextId, "Busque algo que sempre falha.");
        }
        finally
        {
            await host.StopAsync();
        }

        // A task termina failed (não trava o worker, não fica pendente para
        // sempre) — mesmo comportamento já existente para qualquer outra
        // exceção não tratada dentro de ExecuteAsync.
        Assert.Equal(nameof(TaskState.Failed), record.State);
    }

    /// <summary>
    /// Constrói um mock de <see cref="IChatClient"/> que pede
    /// <paramref name="toolCallCount"/> chamadas sequenciais da mesma tool
    /// (<paramref name="toolFullName"/>, já com o prefixo de servidor, ver
    /// design.md Decision 3) antes de responder com texto final. A resposta
    /// final incorpora o resultado de todas as tool calls recebidas nas
    /// mensagens — prova de que o resultado real da tool (via
    /// <see cref="FakeMcpServerHttpMessageHandler"/>) chegou de volta ao LLM.
    /// </summary>
    private static Mock<IChatClient> BuildToolCallingChatClientMock(
        string toolFullName,
        int toolCallCount,
        out Func<string?> finalResponseTextCapture)
    {
        var mock = new Mock<IChatClient>();
        var callIndex = 0;
        string? finalText = null;

        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.Is<ChatOptions?>(o => o != null),
                It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> messages, ChatOptions? _, CancellationToken _) =>
            {
                callIndex++;
                if (callIndex <= toolCallCount)
                {
                    var callId = $"call-{callIndex}";
                    return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                        new List<AIContent> { new FunctionCallContent(callId, toolFullName, new Dictionary<string, object?>()) })));
                }

                var toolResults = messages
                    .SelectMany(m => m.Contents)
                    .OfType<FunctionResultContent>()
                    .Select(r => r.Result?.ToString())
                    .Where(text => text is not null);

                finalText = $"resposta final incluindo: {string.Join(", ", toolResults)}";
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, finalText)));
            });

        finalResponseTextCapture = () => finalText;
        return mock;
    }

    private static string UniqueServerUrl() => $"https://fake-mcp-{Guid.NewGuid():N}.test/mcp";

    private IHost BuildHost(IChatClient chatClient, FakeMcpServerHttpMessageHandler mcpHandler)
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

        builder.Services.Configure<McpCryptoOptions>(options => options.CredentialEncryptionKey = EncryptionKey);

        var resolverMock = new Mock<IChatClientResolver>();
        resolverMock.Setup(resolver => resolver.Resolve(It.IsAny<string>(), It.IsAny<string>())).Returns(chatClient);
        builder.Services.AddSingleton(resolverMock.Object);

        builder.Services.AddSingleton<IMcpCredentialCipher, AesGcmMcpCredentialCipher>();
        builder.Services.AddHttpClient(McpTransportFactory.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => mcpHandler);
        builder.Services.AddSingleton<McpTransportFactory>();
        builder.Services.AddSingleton<IMcpToolSetResolver, McpToolSetResolver>();

        builder.Services.AddSingleton<AgentExecutionService>();
        builder.Services.AddHostedService<TaskJobConsumer>();

        return builder.Build();
    }

    private async Task SeedAgentAsync(Guid agentId, string agentName = "Atendente", string instructions = "Responda com simpatia.", string provider = "openai", string model = "gpt-5.6-sol")
    {
        await using var dbContext = CreateDbContext();

        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "Provider", "Model", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {agentName}, {instructions}, {provider}, {model}, {now}, {now})
             """);
    }

    private async Task<Guid> SeedMcpServerAsync(string name, string url, bool isActive = true)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO mcp_servers ("Id", "Name", "Description", "Url", "AuthType", "EncryptedCredential", "IsActive", "CreatedAt", "UpdatedAt")
             VALUES ({id}, {name}, {""}, {url}, {"None"}, {(string?)null}, {isActive}, {now}, {now})
             """);
        return id;
    }

    private async Task SeedAgentMcpServerAsync(Guid agentId, Guid mcpServerId, IReadOnlyList<string> allowedTools)
    {
        var allowedToolsJson = JsonSerializer.Serialize(allowedTools);
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agent_mcp_servers ("AgentId", "McpServerId", allowed_tools)
             VALUES ({agentId}, {mcpServerId}, {allowedToolsJson}::jsonb)
             """);
    }

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Postgres.GetConnectionString()).Options);

    private async Task SeedTaskAsync(string taskId, Guid agentId, string contextId, string userMessage)
    {
        await using var dbContext = CreateDbContext();

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
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await using var dbContext = CreateDbContext();
            var record = await dbContext.A2ATasks.AsNoTracking().FirstOrDefaultAsync(t => t.TaskId == taskId);

            if (record is not null && record.State is nameof(TaskState.Completed) or nameof(TaskState.Failed))
            {
                return record;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Task '{taskId}' não atingiu um estado terminal a tempo.");
    }

    private async Task<A2ATaskRecord> RunTurnAsync(Guid agentId, string contextId, string userMessage)
    {
        var taskId = Guid.NewGuid().ToString("N");
        await SeedTaskAsync(taskId, agentId, contextId, userMessage);
        await PublishJobAsync(taskId, agentId, contextId);
        return await PollUntilTerminalAsync(taskId);
    }
}
