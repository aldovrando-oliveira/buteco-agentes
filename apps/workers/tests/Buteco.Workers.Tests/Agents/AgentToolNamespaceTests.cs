using System.Text.Json;
using global::A2A;
using Buteco.Workers.A2A;
using Buteco.Workers.AgentDelegations;
using Buteco.Workers.Agents;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Mcp;
using Buteco.Workers.Mcp.Entities;
using Buteco.Workers.Mcp.Security;
using Buteco.Workers.Messaging;
using Buteco.Workers.Notifications;
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

namespace Buteco.Workers.Tests.Agents;

/// <summary>
/// Teste de acordo da change dedupe-global-nome-de-tool entre os dois lados que
/// dividem o espaço de nome de tool (convenção 11): as tools MCP resolvidas por
/// <see cref="McpToolSetResolver"/> contra as tools de delegação resolvidas por
/// <see cref="AgentDelegationToolSetResolver"/>.
///
/// A colisão é montada fazendo os DOIS RESOLVEDORES REAIS produzirem o mesmo
/// nome a partir de cadastro real seedado — nunca escrevendo dois nomes iguais à
/// mão numa lista. Um conjunto forjado no teste passaria igual com o
/// comportamento certo e com o errado, que é exatamente o modo de falha que a
/// convenção 11 nomeia e que já mordeu duas vezes nesta base.
///
/// E a asserção é sobre o seam de produção: o <c>ChatOptions.Tools</c> que
/// <see cref="AgentExecutionService"/> monta e entrega ao
/// <see cref="IChatClient"/>, capturado pelo mock. Não sobre uma união
/// reescrita pelo teste.
/// </summary>
[Collection(WorkerHostCollection.Name)]
public class AgentToolNamespaceTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    private const string EncryptionKey = "NtxqjqnKG3sqy52PFRh/SGk573bsE9TrDtKOsDiR8uc=";

    /// <summary>
    /// Nome de agente Target escolhido para que a tool de delegação derivada
    /// ocupe exatamente <see cref="ToolNameSanitizer.MaxToolNameLength"/>
    /// caracteres: "delegate_to_" consome 12, então o slug precisa de 52.
    /// </summary>
    private static readonly string CollidingTargetName = new('a', 52);

    private static string CollidingDelegationToolName =>
        ToolNameSanitizer.Sanitize($"delegate_to_{DelegationToolNameSlugifier.Slugify(CollidingTargetName)}");

    /// <summary>
    /// Nome de McpServer cujo nome composto <c>{servidor}__{tool}</c> passa de
    /// 64 caracteres e, truncado, cai exatamente sobre
    /// <see cref="CollidingDelegationToolName"/>. É o único caminho pelo qual os
    /// dois conjuntos podem colidir (design.md, V5): o slug de delegação nunca
    /// contém "__" e o nome MCP sempre contém, então a separação só se perde
    /// quando a truncagem corta antes do separador.
    /// </summary>
    private static string CollidingServerName => $"{CollidingDelegationToolName}-sufixo-que-sera-truncado";

    [Fact]
    public async Task ExecuteAsync_McpToolNameCollidesWithDelegationToolName_BothNamesSurviveDistinct()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var taskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(sourceId, "Atendente Geral");
        await SeedAgentAsync(targetId, CollidingTargetName);
        await SeedAgentDelegationAsync(sourceId, targetId);

        var handler = new FakeMcpServerHttpMessageHandler();
        var url = UniqueServerUrl();
        handler.ConfigureServer(url, new FakeMcpServerConfig { AvailableTools = [new FakeMcpTool("search")] });
        var serverId = await SeedMcpServerAsync(CollidingServerName, url);
        await SeedAgentMcpServerAsync(sourceId, serverId, ["search"]);

        await SeedTaskAsync(taskId, sourceId, contextId, "Olá.");

        var (chatClient, captured) = BuildToolCapturingChatClient();
        using var host = BuildHost(chatClient, handler);
        await host.StartAsync();
        await PublishJobAsync(taskId, sourceId, contextId);
        await PollUntilTerminalAsync(taskId);
        await host.StopAsync();

        var tools = Assert.Single(captured);

        // Pré-condição do teste: os dois resolvedores REAIS produziram, a partir
        // do cadastro seedado, o mesmo nome pretendido. Se esta asserção falhar,
        // a colisão não foi montada e o resto do teste não prova nada.
        Assert.Equal(2, tools.Count);

        var names = tools.Select(tool => tool.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, name => Assert.True(
            name.Length <= ToolNameSanitizer.MaxToolNameLength,
            $"Nome '{name}' tem {name.Length} caracteres, acima do limite de {ToolNameSanitizer.MaxToolNameLength}."));

        // Decisão 5: a precedência é declarada — MCP mantém o nome pretendido,
        // a tool de delegação é a renomeada.
        Assert.Contains(CollidingDelegationToolName, names);
        var renamed = Assert.Single(names, name => name != CollidingDelegationToolName);
        Assert.StartsWith("delegate_to_", renamed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_NoCollisionInToolSet_PreservesEveryNameExactly()
    {
        // Par "sem colisão" da convenção 5, e contraparte de R1: delimita o
        // alcance da mudança aos agentes que já estavam quebrados. A asserção
        // que importa é a de que NADA muda — é ela que impede um dedupe zeloso
        // demais de renomear o que estava correto.
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var taskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(sourceId, "Atendente Geral");
        await SeedAgentAsync(targetId, "Financeiro");
        await SeedAgentDelegationAsync(sourceId, targetId);

        var handler = new FakeMcpServerHttpMessageHandler();
        var url = UniqueServerUrl();
        handler.ConfigureServer(url, new FakeMcpServerConfig { AvailableTools = [new FakeMcpTool("search")] });
        var serverId = await SeedMcpServerAsync("Reservas", url);
        await SeedAgentMcpServerAsync(sourceId, serverId, ["search"]);

        await SeedTaskAsync(taskId, sourceId, contextId, "Olá.");

        var (chatClient, captured) = BuildToolCapturingChatClient();
        using var host = BuildHost(chatClient, handler);
        await host.StartAsync();
        await PublishJobAsync(taskId, sourceId, contextId);
        await PollUntilTerminalAsync(taskId);
        await host.StopAsync();

        var tools = Assert.Single(captured);
        var names = tools.Select(tool => tool.Name).ToList();

        Assert.Equal(
            new[] { "Reservas__search", "delegate_to_financeiro" },
            names.OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task ExecuteAsync_TwoMcpServersWhoseNamesSanitizeToTheSameString_BothNamesSurviveDistinct()
    {
        // O caminho de colisão realmente alcançável hoje (design.md, V5): dois
        // McpServer.Name que só diferem em caractere fora de [a-zA-Z0-9_-].
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var taskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(agentId, "Atendente Geral");

        var handler = new FakeMcpServerHttpMessageHandler();
        var urlA = UniqueServerUrl();
        var urlB = UniqueServerUrl();
        handler.ConfigureServer(urlA, new FakeMcpServerConfig { AvailableTools = [new FakeMcpTool("search")] });
        handler.ConfigureServer(urlB, new FakeMcpServerConfig { AvailableTools = [new FakeMcpTool("search")] });
        var serverAId = await SeedMcpServerAsync("Zendesk MCP", urlA);
        var serverBId = await SeedMcpServerAsync("Zendesk.MCP", urlB);
        await SeedAgentMcpServerAsync(agentId, serverAId, ["search"]);
        await SeedAgentMcpServerAsync(agentId, serverBId, ["search"]);

        await SeedTaskAsync(taskId, agentId, contextId, "Olá.");

        var (chatClient, captured) = BuildToolCapturingChatClient();
        using var host = BuildHost(chatClient, handler);
        await host.StartAsync();
        await PublishJobAsync(taskId, agentId, contextId);
        await PollUntilTerminalAsync(taskId);
        await host.StopAsync();

        var tools = Assert.Single(captured);
        Assert.Equal(2, tools.Count);
        var names = tools.Select(tool => tool.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("Zendesk_MCP__search", names);
    }

    [Fact]
    public async Task ExecuteAsync_SameCollidingRegistration_ProducesTheSameNames_AcrossExecutions()
    {
        // Requisito "Conjunto de nomes estável entre execuções", com colisão: a
        // MESMA tool tem de ser a renomeada nas duas execuções, e não só os
        // nomes coincidirem como conjunto.
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(agentId, "Atendente Geral");

        var handler = new FakeMcpServerHttpMessageHandler();
        var urlA = UniqueServerUrl();
        var urlB = UniqueServerUrl();
        handler.ConfigureServer(urlA, new FakeMcpServerConfig { AvailableTools = [new FakeMcpTool("search")] });
        handler.ConfigureServer(urlB, new FakeMcpServerConfig { AvailableTools = [new FakeMcpTool("search")] });
        var serverAId = await SeedMcpServerAsync("Zendesk MCP", urlA);
        var serverBId = await SeedMcpServerAsync("Zendesk.MCP", urlB);
        await SeedAgentMcpServerAsync(agentId, serverAId, ["search"]);
        await SeedAgentMcpServerAsync(agentId, serverBId, ["search"]);

        var (chatClient, captured) = BuildToolCapturingChatClient();
        using var host = BuildHost(chatClient, handler);
        await host.StartAsync();

        foreach (var _ in Enumerable.Range(0, 2))
        {
            var taskId = Guid.NewGuid().ToString("N");
            await SeedTaskAsync(taskId, agentId, contextId, "Olá de novo.");
            await PublishJobAsync(taskId, agentId, contextId);
            await PollUntilTerminalAsync(taskId);
        }

        await host.StopAsync();

        Assert.Equal(2, captured.Count);
        Assert.Equal(
            captured[0].Select(tool => tool.Name).ToList(),
            captured[1].Select(tool => tool.Name).ToList());
    }

    [Fact]
    public async Task ExecuteAsync_TwoDelegationTargetsWithCollidingName_BothNamesSurviveDistinct()
    {
        // Contraparte de R4: o comportamento que o requisito "Nome estável e sem
        // colisão para a tool de delegação" garante continua garantido depois de
        // o dedupe sair de AgentDelegationToolSetResolver e virar global. Se
        // este cenário reprovar, a mudança de dono perdeu comportamento.
        var sourceId = Guid.NewGuid();
        var targetAId = Guid.NewGuid();
        var targetBId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var taskId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(sourceId, "Atendente Geral");
        await SeedAgentAsync(targetAId, "Atendimento");
        await SeedAgentAsync(targetBId, "Atendimento");
        await SeedAgentDelegationAsync(sourceId, targetAId);
        await SeedAgentDelegationAsync(sourceId, targetBId);

        await SeedTaskAsync(taskId, sourceId, contextId, "Olá.");

        var (chatClient, captured) = BuildToolCapturingChatClient();
        using var host = BuildHost(chatClient, new FakeMcpServerHttpMessageHandler());
        await host.StartAsync();
        await PublishJobAsync(taskId, sourceId, contextId);
        await PollUntilTerminalAsync(taskId);
        await host.StopAsync();

        var names = captured.Single().Select(tool => tool.Name).ToList();

        Assert.Equal(new[] { "delegate_to_atendimento", "delegate_to_atendimento-2" }, names);
    }

    [Fact]
    public async Task ExecuteAsync_HistoryReferencesToolNoLongerInToolSet_TaskStillCompletes()
    {
        // Caracterização de R7, e DEVE passar com o código de hoje — não é
        // guarda de defeito. Estabelece que o pipeline local
        // (FunctionInvokingChatClient + ChatClientAgent) é transparente a um
        // histórico que referencia função ausente da lista atual de tools,
        // isolando a pergunta de R7 ao provedor, que é o que a tarefa 4.7(a) vai
        // observar quando houver chave.
        //
        // O histórico é produzido de verdade, não forjado: a primeira execução
        // chama a tool MCP real (então a sessão persistida ganha
        // FunctionCallContent/FunctionResultContent com o nome daquela
        // execução), e antes da segunda o vínculo é removido do cadastro — que é
        // exatamente a forma que uma renomeação produz.
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");

        await SeedAgentAsync(agentId, "Atendente Geral");

        var handler = new FakeMcpServerHttpMessageHandler();
        var url = UniqueServerUrl();
        handler.ConfigureServer(url, new FakeMcpServerConfig { AvailableTools = [new FakeMcpTool("search")] });
        var serverId = await SeedMcpServerAsync("Reservas", url);
        await SeedAgentMcpServerAsync(agentId, serverId, ["search"]);

        var firstTaskId = Guid.NewGuid().ToString("N");
        await SeedTaskAsync(firstTaskId, agentId, contextId, "Busque disponibilidade.");

        var chatClient = BuildToolCallingThenAnsweringChatClient("Reservas__search");
        using var host = BuildHost(chatClient, handler);
        await host.StartAsync();
        await PublishJobAsync(firstTaskId, agentId, contextId);
        var first = await PollUntilTerminalAsync(firstTaskId);
        Assert.Equal(nameof(TaskState.Completed), first.State);

        // A tool sai do conjunto — o histórico persistido continua citando-a.
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""DELETE FROM agent_mcp_servers WHERE "AgentId" = {agentId}""");
        }

        var secondTaskId = Guid.NewGuid().ToString("N");
        await SeedTaskAsync(secondTaskId, agentId, contextId, "E agora, sem a tool?");
        await PublishJobAsync(secondTaskId, agentId, contextId);
        var second = await PollUntilTerminalAsync(secondTaskId);
        await host.StopAsync();

        Assert.Equal(nameof(TaskState.Completed), second.State);
    }

    private static (IChatClient Client, List<IList<AITool>> CapturedTools) BuildToolCapturingChatClient()
    {
        var captured = new List<IList<AITool>>();
        var mock = new Mock<IChatClient>();
        mock
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> _, ChatOptions? options, CancellationToken _) =>
            {
                if (options?.Tools is not null)
                {
                    captured.Add(options.Tools);
                }

                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Pronto.")));
            });

        return (mock.Object, captured);
    }

    private static IChatClient BuildToolCallingThenAnsweringChatClient(string toolName)
    {
        var callCount = 0;
        var mock = new Mock<IChatClient>();
        mock
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> _, ChatOptions? options, CancellationToken _) =>
            {
                var isFirstTurn = Interlocked.Increment(ref callCount) == 1;
                var toolIsAvailable = options?.Tools?.Any(tool => tool.Name == toolName) == true;

                if (isFirstTurn && toolIsAvailable)
                {
                    return Task.FromResult(new ChatResponse(new ChatMessage(
                        ChatRole.Assistant,
                        new List<AIContent> { new FunctionCallContent("call-1", toolName, new Dictionary<string, object?>()) })));
                }

                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Pronto.")));
            });

        return mock.Object;
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

        // Os DOIS resolvedores reais — é o ponto do teste de acordo. Nenhum
        // Null*Resolver aqui.
        builder.Services.AddSingleton<IMcpCredentialCipher, AesGcmMcpCredentialCipher>();
        builder.Services.AddHttpClient(McpTransportFactory.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => mcpHandler);
        builder.Services.AddSingleton<McpTransportFactory>();
        builder.Services.AddSingleton<IMcpToolSetResolver, McpToolSetResolver>();
        builder.Services.AddSingleton<ITaskJobPublisher, RabbitMqTaskJobPublisher>();
        builder.Services.AddSingleton<IAgentDelegationToolSetResolver, AgentDelegationToolSetResolver>();
        builder.Services.Configure<AgentDelegationToolOptions>(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(5);
            options.PollInterval = TimeSpan.FromMilliseconds(200);
        });

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHttpClient(PushNotificationSender.HttpClientName)
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));
        builder.Services.AddSingleton<PushNotificationSender>();
        builder.Services.AddSingleton<ToolNameDeduplicator>();
        builder.Services.AddSingleton<AgentExecutionService>();
        builder.Services.AddHostedService<TaskJobConsumer>();

        return builder.Build();
    }

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fixture.Postgres.GetConnectionString()).Options);

    private async Task SeedAgentAsync(Guid agentId, string agentName)
    {
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "IsActive", "Provider", "Model", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {agentName}, {"Instruções de teste."}, {true}, {"openai"}, {"gpt-5.6-sol"}, {now}, {now})
             """);
    }

    private async Task SeedAgentDelegationAsync(Guid sourceAgentId, Guid targetAgentId)
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agent_delegations ("SourceAgentId", "TargetAgentId")
             VALUES ({sourceAgentId}, {targetAgentId})
             """);
    }

    private async Task<Guid> SeedMcpServerAsync(string name, string url)
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO mcp_servers ("Id", "Name", "Description", "Url", "AuthType", "EncryptedCredential", "IsActive", "CreatedAt", "UpdatedAt")
             VALUES ({id}, {name}, {""}, {url}, {nameof(McpServerAuthType.None)}, {(string?)null}, {true}, {now}, {now})
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

    private async Task SeedTaskAsync(string taskId, Guid agentId, string contextId, string userMessage)
    {
        var message = new Message
        {
            Role = Role.User,
            Parts = [Part.FromText(userMessage)],
            MessageId = Guid.NewGuid().ToString("N"),
            ContextId = contextId,
        };

        var task = new AgentTask
        {
            Id = taskId,
            ContextId = contextId,
            Status = new TaskStatus { State = TaskState.Submitted, Timestamp = DateTimeOffset.UtcNow },
            History = [message],
        };

        var store = new PostgresTaskStore(
            new ScopeFactoryFromFixture(fixture.Postgres.GetConnectionString()),
            agentId);
        await store.SaveTaskAsync(taskId, task, CancellationToken.None);
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

        var body = JsonSerializer.SerializeToUtf8Bytes(new TaskJobMessage(taskId, agentId, contextId));
        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: TaskJobConsumer.QueueName,
            mandatory: true,
            basicProperties: new BasicProperties { Persistent = true },
            body: body);
    }

    private async Task<A2ATaskRecord> PollUntilTerminalAsync(string taskId, int maxAttempts = 60)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            await using var dbContext = CreateDbContext();
            var record = await dbContext.A2ATasks.AsNoTracking().FirstOrDefaultAsync(t => t.TaskId == taskId);

            if (record is not null && record.State is nameof(TaskState.Completed) or nameof(TaskState.Failed) or nameof(TaskState.Rejected))
            {
                return record;
            }

            await Task.Delay(250);
        }

        Assert.Fail($"Task {taskId} não alcançou estado terminal.");
        throw new InvalidOperationException("unreachable");
    }

    private sealed class ScopeFactoryFromFixture(string connectionString) : IServiceScopeFactory
    {
        private readonly IServiceProvider provider = new ServiceCollection()
            .AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString))
            .BuildServiceProvider();

        public IServiceScope CreateScope() => provider.CreateScope();
    }
}
