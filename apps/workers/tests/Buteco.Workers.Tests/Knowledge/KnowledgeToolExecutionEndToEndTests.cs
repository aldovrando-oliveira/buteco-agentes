using System.Text.Json;
using global::A2A;
using Buteco.Workers.A2A;
using Buteco.Workers.AgentDelegations;
using Buteco.Workers.Agents;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Knowledge.Embedding;
using Buteco.Workers.Knowledge.Execution;
using Buteco.Workers.Mcp;
using Buteco.Workers.Messaging;
using Buteco.Workers.Notifications;
using Buteco.Workers.Options;
using Buteco.Workers.Tests.Knowledge.Support;
using Buteco.Workers.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using RabbitMQ.Client;
using TaskStatus = A2A.TaskStatus;

namespace Buteco.Workers.Tests.Knowledge;

/// <summary>
/// Cobre a etapa 4 fim a fim: pipeline real (RabbitMQ + Postgres +
/// <see cref="TaskJobConsumer"/> + <see cref="AgentExecutionService"/>) com a
/// tool de conhecimento real sendo chamada pelo <c>FunctionInvokingChatClient</c>
/// que o <c>ChatClientAgent</c> insere, e o trecho recuperado chegando à
/// resposta final da task. Mesmo molde de
/// <c>Mcp/McpToolExecutionEndToEndTests</c>.
///
/// <para>
/// <b>O que é real aqui e o que não é:</b> o resolvedor, a tool, a consulta
/// vetorial, o índice, o pipeline de tool-calling e a task são reais. Só o
/// <c>IChatClient</c> e o <b>provedor de embedding</b> são falsos — os dois
/// exigiriam chave e rede, e nenhum dos dois é o que esta change construiu.
/// </para>
/// </summary>
[Collection(WorkerHostCollection.Name)]
public class KnowledgeToolExecutionEndToEndTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    private const int Dimensions = 4096;
    private const string ConteudoQueSoExisteNaBase =
        "O prazo de carência para reemissão é de onze dias úteis, contados do protocolo.";

    /// <summary>
    /// <b>É o cenário que fecha a etapa, e o que o teste manual da 5a-2 não
    /// conseguiu produzir:</b> um agente com base vinculada e índice povoado
    /// responde citando conteúdo que <b>só</b> existe na base. Antes desta
    /// change, vincular uma base a um agente não fazia nada em tempo de
    /// execução — o agente recebia dois conjuntos de tools e nenhum deles
    /// alcançava o índice.
    /// </summary>
    [Fact]
    public async Task RoundTrip_AgentWithLinkedBase_AnswersWithContentThatOnlyExistsInTheIndex()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");

        var baseId = await SeedBaseAsync("Políticas de Cobrança", "Prazos, descontos e reemissão de títulos.");
        var documentId = await SeedDocumentAsync(baseId, "Política de reemissão");
        await SeedFragmentAsync(baseId, documentId, 0, ConteudoQueSoExisteNaBase, Vector(0.0));
        await SeedFragmentAsync(baseId, documentId, 1, "Assunto distante, para haver o que ordenar.", Vector(0.9));
        await SeedAgentAsync(agentId);
        await SeedLinkAsync(agentId, baseId);

        var chatClient = BuildToolCallingChatClientMock("search_politicas-de-cobranca", out var finalText);

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        A2ATaskRecord record;
        try
        {
            record = await RunTurnAsync(agentId, contextId, "Qual o prazo de carência para reemissão?");
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.Equal(nameof(TaskState.Completed), record.State);
        Assert.NotNull(finalText());
        Assert.Contains("onze dias úteis", finalText()!, StringComparison.Ordinal);

        // E a distância chegou junto do trecho, que é o que sustenta a decisão
        // de não haver limiar: o agente recebe a ordenação e decide.
        Assert.Contains("distancia", finalText()!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Par "sem item" no nível do pipeline: sem base vinculada, a execução segue
    /// exatamente como antes desta change — nenhuma tool de conhecimento, nenhum
    /// erro, nenhuma consulta desperdiçada.
    /// </summary>
    [Fact]
    public async Task RoundTrip_AgentWithoutLinkedBase_ReceivesNoKnowledgeTool()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        await SeedAgentAsync(agentId);

        var (chatClient, captured) = BuildToolCapturingChatClient();

        using var host = BuildHost(chatClient);
        await host.StartAsync();
        try
        {
            var record = await RunTurnAsync(agentId, contextId, "Olá.");
            Assert.Equal(nameof(TaskState.Completed), record.State);
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.Empty(Assert.Single(captured));
    }

    /// <summary>
    /// Degradação graciosa no pipeline real (convenção 4): o provedor de
    /// embedding estoura dentro da invocação da tool, a tool devolve resultado
    /// de falha ao modelo, e a <b>task conclui</b> — não falha.
    /// </summary>
    [Fact]
    public async Task RoundTrip_EmbeddingProviderFails_TaskStillCompletes()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");

        var baseId = await SeedBaseAsync("Cobrança", "Prazos e descontos.");
        await SeedAgentAsync(agentId);
        await SeedLinkAsync(agentId, baseId);

        var chatClient = BuildToolCallingChatClientMock("search_cobranca", out var finalText);

        using var host = BuildHost(chatClient.Object, new ThrowingEmbeddingGeneratorResolver());
        await host.StartAsync();

        A2ATaskRecord record;
        try
        {
            record = await RunTurnAsync(agentId, contextId, "Qual o prazo?");
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.Equal(nameof(TaskState.Completed), record.State);
        Assert.Contains("Não foi possível consultar", finalText()!, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ apoio

    private static readonly JsonSerializerOptions RelaxedJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static Mock<IChatClient> BuildToolCallingChatClientMock(string toolName, out Func<string?> finalTextCapture)
    {
        var mock = new Mock<IChatClient>();
        var callIndex = 0;
        string? finalText = null;

        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.Is<ChatOptions?>(o => o != null), It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> messages, ChatOptions? _, CancellationToken _) =>
            {
                callIndex++;
                if (callIndex == 1)
                {
                    return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                        new List<AIContent>
                        {
                            new FunctionCallContent("call-1", toolName, new Dictionary<string, object?>
                            {
                                ["consulta"] = "prazo de carência para reemissão",
                            }),
                        })));
                }

                // O texto final inclui o resultado CRU da tool — é assim que o
                // teste prova que o trecho recuperado atravessou o pipeline até
                // a resposta, em vez de ter sido inventado pelo mock.
                // Serializa com escaping relaxado de propósito: com o encoder
                // default, "úteis" sai como "\u00FAteis" e a asserção do teste
                // procuraria uma cadeia que o modelo receberia decodificada.
                // Foi o que reprovou na primeira execução — artefato do apoio,
                // não defeito do código.
                var toolResults = messages
                    .SelectMany(m => m.Contents)
                    .OfType<FunctionResultContent>()
                    .Select(r => r.Result is null ? null : JsonSerializer.Serialize(r.Result, RelaxedJson))
                    .Where(text => text is not null);

                finalText = $"resposta final incluindo: {string.Join(", ", toolResults)}";
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, finalText)));
            });

        finalTextCapture = () => finalText;
        return mock;
    }

    private static (IChatClient Client, List<IReadOnlyList<AITool>> Captured) BuildToolCapturingChatClient()
    {
        var captured = new List<IReadOnlyList<AITool>>();
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> _, ChatOptions? options, CancellationToken _) =>
            {
                captured.Add(options?.Tools?.OfType<AITool>().ToList() ?? []);
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
            });

        return (mock.Object, captured);
    }

    private IHost BuildHost(IChatClient chatClient, IEmbeddingGeneratorResolver? embeddings = null)
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

        // Resolvedor de conhecimento REAL — é o ponto do teste. MCP e delegação
        // ficam nulos porque não são o que esta change construiu.
        builder.Services.AddSingleton<IMcpToolSetResolver, NullMcpToolSetResolver>();
        builder.Services.AddSingleton<IAgentDelegationToolSetResolver, NullAgentDelegationToolSetResolver>();
        builder.Services.AddSingleton<IKnowledgeToolSetResolver, KnowledgeToolSetResolver>();
        builder.Services.AddSingleton(embeddings ?? new FakeEmbeddingGeneratorResolver(Vector(0.0)));

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHttpClient(PushNotificationSender.HttpClientName)
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));
        builder.Services.AddSingleton<PushNotificationSender>();
        builder.Services.AddSingleton<ToolNameDeduplicator>();
        builder.Services.AddSingleton<AgentExecutionService>();
        builder.Services.AddHostedService<TaskJobConsumer>();

        return builder.Build();
    }

    private static float[] Vector(double segundoEixo)
    {
        var v = new float[Dimensions];
        v[0] = 1f;
        v[1] = (float)segundoEixo;
        return v;
    }

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()).Options);

    private async Task<A2ATaskRecord> RunTurnAsync(Guid agentId, string contextId, string userMessage)
    {
        var taskId = Guid.NewGuid().ToString("N");
        await SeedTaskAsync(taskId, agentId, contextId, userMessage);
        await PublishJobAsync(taskId, agentId, contextId);
        return await PollUntilTerminalAsync(taskId);
    }

    private async Task SeedAgentAsync(Guid agentId)
    {
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "IsActive", "Provider", "Model", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {"Atendente"}, {"Responda com simpatia."}, {true}, {"openai"}, {"gpt-5.6-sol"}, {now}, {now})
             """);
    }

    private async Task<Guid> SeedBaseAsync(string name, string description)
    {
        var id = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO knowledge_bases ("Id", "Name", "Description", "IsActive", "CreatedAt", "UpdatedAt")
             VALUES ({id}, {name}, {description}, {true}, {now}, {now})
             """);
        return id;
    }

    private async Task<Guid> SeedDocumentAsync(Guid knowledgeBaseId, string title)
    {
        var id = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO knowledge_documents
               ("Id", "KnowledgeBaseId", "Title", "SourceType", "ExtractedText", "IndexingStatus",
                "ContentRevision", "FragmentCount", "IndexingAttempts", "CreatedAt", "UpdatedAt")
             VALUES ({id}, {knowledgeBaseId}, {title}, {"markdown"}, {"texto"}, {0}, {1}, {0}, {0}, {now}, {now})
             """);
        return id;
    }

    private async Task SeedFragmentAsync(Guid knowledgeBaseId, Guid documentId, int ordinal, string text, float[] embedding)
    {
        await using var dbContext = CreateDbContext();
        var vector = new Pgvector.Vector(embedding);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO knowledge_fragments
               ("Id", "KnowledgeDocumentId", "KnowledgeBaseId", "Ordinal", "Text", "Embedding",
                "EmbeddingProvider", "EmbeddingModel", "EmbeddingDimensions", "CreatedAt")
             VALUES ({Guid.NewGuid()}, {documentId}, {knowledgeBaseId}, {ordinal}, {text}, {vector},
                     {"openai"}, {"qwen-qwen3-embedding-8b"}, {Dimensions}, {DateTimeOffset.UtcNow})
             """);
    }

    private async Task SeedLinkAsync(Guid agentId, Guid knowledgeBaseId)
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agent_knowledge_bases ("AgentId", "KnowledgeBaseId")
             VALUES ({agentId}, {knowledgeBaseId})
             """);
    }

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

        var body = JsonSerializer.SerializeToUtf8Bytes(new TaskJobMessage(taskId, agentId, contextId));
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
}
