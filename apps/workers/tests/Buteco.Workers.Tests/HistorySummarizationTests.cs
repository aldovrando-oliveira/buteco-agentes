using System.Text.Json;
using global::A2A;
using Buteco.Workers.A2A;
using Buteco.Workers.AgentDelegations;
using Buteco.Workers.Agents;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Mcp;
using Buteco.Workers.Messaging;
using Buteco.Workers.Notifications;
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
/// Cobre a change apps-workers-resumo-historico-conversa: resumo incremental
/// do histórico de conversa via <c>CompactionProvider</c>/
/// <c>SummarizationCompactionStrategy</c> (ver design.md e
/// specs/a2a-task-lifecycle/spec.md daquela change).
/// </summary>
[Collection(WorkerHostCollection.Name)]
public class HistorySummarizationTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    // Devem bater com as constantes privadas de AgentExecutionService (não
    // vale a pena expor via InternalsVisibleTo só para os testes). Se os
    // valores lá mudarem, estes testes precisam acompanhar.
    private const int SummarizationTurnThreshold = 10;
    private const int MaxHistoryMessages = 200;

    [Fact]
    public async Task BelowThreshold_NoSummarizationCallHappens()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var turns = SummarizationTurnThreshold - 1;

        await SeedAgentAsync(agentId);

        var summaryCallInputs = new List<List<ChatMessage>>();
        List<ChatMessage>? lastMainCallMessages = null;
        var chatClient = BuildSummarizingChatClientMock(summaryCallInputs, out var mainCallCapture);

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            for (var turn = 1; turn <= turns; turn++)
            {
                var record = await RunTurnAsync(agentId, contextId, $"pergunta {turn}");
                Assert.Equal(nameof(TaskState.Completed), record.State);
            }
        }
        finally
        {
            await host.StopAsync();
        }

        lastMainCallMessages = mainCallCapture();

        // Comportamento idêntico à change anterior, sem regressão: nenhuma
        // chamada de resumo, histórico cru completo chega ao LLM.
        Assert.Empty(summaryCallInputs);
        Assert.NotNull(lastMainCallMessages);
        Assert.Contains(lastMainCallMessages!, m => m.Text == "pergunta 1");
        Assert.Contains(lastMainCallMessages!, m => m.Text == $"pergunta {turns}");
    }

    [Fact]
    public async Task CrossingThreshold_TriggersSummarization_AndMainCallReceivesSummaryPlusRecentTurns()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var turns = SummarizationTurnThreshold + 1;

        await SeedAgentAsync(agentId);

        var summaryCallInputs = new List<List<ChatMessage>>();
        var chatClient = BuildSummarizingChatClientMock(summaryCallInputs, out var mainCallCapture);

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            for (var turn = 1; turn <= turns; turn++)
            {
                var record = await RunTurnAsync(agentId, contextId, $"pergunta {turn}");
                Assert.Equal(nameof(TaskState.Completed), record.State);
            }
        }
        finally
        {
            await host.StopAsync();
        }

        var lastMainCallMessages = mainCallCapture();

        // (a) uma chamada de resumo foi feita ao cruzar o limiar.
        Assert.NotEmpty(summaryCallInputs);

        // (b) a chamada de conversa real recebe o resumo + turnos recentes
        // preservados no lugar do histórico cru completo da porção mais
        // antiga: a primeira pergunta (bem além do que MinimumPreservedGroups
        // preserva cru) já foi resumida e não chega mais crua ao LLM; a
        // pergunta mais recente continua presente.
        Assert.NotNull(lastMainCallMessages);
        Assert.Contains(lastMainCallMessages!, m => m.Text != null && m.Text.StartsWith("[Summary]"));
        Assert.DoesNotContain(lastMainCallMessages!, m => m.Text == "pergunta 1");
        Assert.Contains(lastMainCallMessages!, m => m.Text == $"pergunta {turns}");
    }

    [Fact]
    public async Task IncrementalSummarization_AcrossManyTriggers_PreviousSummaryFeedsIntoNextSummarizationCall()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        // Margem generosa para garantir vários cruzamentos do limiar
        // (MinimumPreservedGroups padrão do pacote é 8 grupos ~ 4 turnos,
        // então o gatilho volta a disparar a cada poucos turnos depois do
        // primeiro resumo) — este é o teste que teria pego a regressão
        // achada via /opsx:explore antes do apply (design.md, Decisão 9):
        // CompactionMessageIndex.Update descarta todo o bookkeeping
        // acumulado sempre que RecentMessageChatReducer trunca a lista de
        // mensagens que o alimenta, então um teste de só um ou dois
        // gatilhos não é suficiente para provar estabilidade.
        var turns = 40;

        await SeedAgentAsync(agentId);

        var summaryCallInputs = new List<List<ChatMessage>>();
        var chatClient = BuildSummarizingChatClientMock(summaryCallInputs, out _);

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            for (var turn = 1; turn <= turns; turn++)
            {
                var record = await RunTurnAsync(agentId, contextId, $"pergunta {turn}");
                Assert.Equal(nameof(TaskState.Completed), record.State);
            }
        }
        finally
        {
            await host.StopAsync();
        }

        // Múltiplos gatilhos de resumo ao longo da mesma conversa — prova
        // de que o mecanismo não morre depois da primeira vez.
        Assert.True(summaryCallInputs.Count >= 3, $"Esperava pelo menos 3 chamadas de resumo, houve {summaryCallInputs.Count}.");

        // O segundo gatilho de resumo recebe o resumo anterior entre as
        // mensagens enviadas para o LLM — prova de que o estado do
        // CompactionProvider não foi resetado silenciosamente entre o
        // primeiro e o segundo gatilho (o bug que a investigação achou).
        var secondSummaryCallInput = summaryCallInputs[1];
        Assert.Contains(secondSummaryCallInput, m => m.Text != null && m.Text.StartsWith("[Summary]"));

        // A primeira pergunta, já resumida no primeiro gatilho, nunca mais
        // reaparece como mensagem crua em nenhuma chamada de resumo
        // seguinte — prova de que não é "resumir do zero a cada gatilho".
        foreach (var laterCallInput in summaryCallInputs.Skip(1))
        {
            Assert.DoesNotContain(laterCallInput, m => m.Text == "pergunta 1");
        }
    }

    [Fact]
    public async Task SummarizationCallFailure_DoesNotPreventUserTurnFromCompleting()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var turns = SummarizationTurnThreshold + 2;

        await SeedAgentAsync(agentId);

        var chatClient = new Mock<IChatClient>();

        // Chamada de resumo (ChatOptions null, ver
        // SummarizationCompactionStrategy.CompactCoreAsync decompilado)
        // sempre falha, simulando o provider do agente indisponível.
        chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.Is<ChatOptions?>(o => o == null),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Provider de resumo indisponível"));

        // Chamada de conversa real (ChatOptions não-null) continua
        // funcionando normalmente.
        chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.Is<ChatOptions?>(o => o != null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            for (var turn = 1; turn <= turns; turn++)
            {
                var record = await RunTurnAsync(agentId, contextId, $"pergunta {turn}");

                // (a) e (b): mesmo cruzando o limiar (turno
                // SummarizationTurnThreshold + 1 em diante, onde a chamada
                // de resumo falha), a task do turno corrente ainda assim
                // chega a completed — a falha na chamada de resumo não é
                // tratada como falha da task inteira.
                Assert.Equal(nameof(TaskState.Completed), record.State);
            }
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task CompactionState_SurvivesRealSerializeDeserializeRoundTrip_AcrossSeparateWorkerInstances()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var turnsBeforeRestart = SummarizationTurnThreshold + 1;

        await SeedAgentAsync(agentId);

        var summaryCallInputsA = new List<List<ChatMessage>>();
        var chatClientA = BuildSummarizingChatClientMock(summaryCallInputsA, out _);

        // Instância A: processa turnos suficientes para cruzar o limiar de
        // resumo uma vez, depois é totalmente descartada (host parado e
        // disposed) — nenhum objeto compartilhado com a instância B além do
        // Postgres do fixture (mesmo padrão de prova de "sem estado em
        // memória" já usado em ConversationHistoryTests).
        using (var hostA = BuildHost(chatClientA.Object))
        {
            await hostA.StartAsync();

            for (var turn = 1; turn <= turnsBeforeRestart; turn++)
            {
                var record = await RunTurnAsync(agentId, contextId, $"pergunta {turn}");
                Assert.Equal(nameof(TaskState.Completed), record.State);
            }

            await hostA.StopAsync();
        }

        Assert.NotEmpty(summaryCallInputsA);

        // Instância B: construída do zero, nunca teve acesso a nenhum
        // objeto/mock da instância A — só pode ver o resumo gerado por A se
        // ele realmente sobreviveu ao ciclo SerializeSessionAsync (fim de
        // A) -> Postgres -> DeserializeSessionAsync (início de B).
        var summaryCallInputsB = new List<List<ChatMessage>>();
        var chatClientB = BuildSummarizingChatClientMock(summaryCallInputsB, out var mainCallCaptureB);

        using (var hostB = BuildHost(chatClientB.Object))
        {
            await hostB.StartAsync();
            var record = await RunTurnAsync(agentId, contextId, "pergunta depois do restart");
            Assert.Equal(nameof(TaskState.Completed), record.State);
            await hostB.StopAsync();
        }

        var firstMainCallMessagesInB = mainCallCaptureB();

        // A primeiríssima chamada de conversa real da instância B já
        // recebe o resumo produzido pela instância A, e não a pergunta 1
        // crua — prova de que o estado incremental do CompactionProvider
        // (não só o histórico bruto) sobreviveu ao round-trip real de
        // serialização entre processos distintos.
        Assert.NotNull(firstMainCallMessagesInB);
        Assert.Contains(firstMainCallMessagesInB!, m => m.Text != null && m.Text.StartsWith("[Summary]"));
        Assert.DoesNotContain(firstMainCallMessagesInB!, m => m.Text == "pergunta 1");
    }

    [Fact]
    public async Task ConversationExceedingMaxHistoryMessages_DegradesGracefully_WithoutFailingTasks()
    {
        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        // Margem além de MaxHistoryMessages (mesmo estilo de margem já
        // usado em ConversationHistoryTests.HistoryExceedingLimit_...):
        // além do teto de segurança da Decisão 9 do design.md, o
        // RecentMessageChatReducer volta a truncar ativamente, o que
        // reabre a colisão com o bookkeeping do CompactionProvider
        // (Groups.Clear() a cada turno) — o objetivo aqui não é provar que
        // o resumo continua funcionando nessa faixa (Risks/Trade-offs do
        // design.md documenta que não continua), e sim que o worker não
        // lança exceção nem falha a task por causa disso: degrada de volta
        // para o comportamento de truncamento puro pré-existente, sem
        // crash.
        var turns = (MaxHistoryMessages / 2) + 3;

        await SeedAgentAsync(agentId);

        var summaryCallInputs = new List<List<ChatMessage>>();
        var chatClient = BuildSummarizingChatClientMock(summaryCallInputs, out _);

        using var host = BuildHost(chatClient.Object);
        await host.StartAsync();

        try
        {
            for (var turn = 1; turn <= turns; turn++)
            {
                var record = await RunTurnAsync(agentId, contextId, $"pergunta {turn}");

                // Nenhum crash / task failed por causa da colisão entre
                // truncamento e compactação além do teto de segurança —
                // comportamento aceito como o limite conhecido (Risks do
                // design.md), não como bug.
                Assert.Equal(nameof(TaskState.Completed), record.State);
            }
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// Constrói um mock de <see cref="IChatClient"/> capaz de distinguir a
    /// chamada de resumo do <c>SummarizationCompactionStrategy</c> (sempre
    /// invocada com <c>ChatOptions</c> nulo — confirmado decompilando
    /// <c>CompactCoreAsync</c>: <c>ChatClient.GetResponseAsync(list, null,
    /// cancellationToken)</c>) da chamada de conversa real do
    /// <c>ChatClientAgent</c> (sempre invocada com <c>ChatOptions</c>
    /// não-nulo, construído a partir de <c>ChatClientAgentOptions.ChatOptions</c>).
    /// </summary>
    private static Mock<IChatClient> BuildSummarizingChatClientMock(
        List<List<ChatMessage>> summaryCallInputs,
        out Func<List<ChatMessage>?> mainCallCapture,
        string summaryText = "resumo simulado da conversa",
        string mainReplyText = "ok")
    {
        var mock = new Mock<IChatClient>();
        List<ChatMessage>? lastMainCallMessages = null;

        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.Is<ChatOptions?>(o => o == null),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (messages, _, _) => summaryCallInputs.Add(messages.ToList()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, summaryText)));

        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.Is<ChatOptions?>(o => o != null),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (messages, _, _) => lastMainCallMessages = messages.ToList())
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, mainReplyText)));

        mainCallCapture = () => lastMainCallMessages;
        return mock;
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
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHttpClient(PushNotificationSender.HttpClientName)
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));
        builder.Services.AddSingleton<PushNotificationSender>();
        builder.Services.AddSingleton<ToolNameDeduplicator>();
        builder.Services.AddSingleton<AgentExecutionService>();
        builder.Services.AddHostedService<TaskJobConsumer>();

        return builder.Build();
    }

    private async Task SeedAgentAsync(Guid agentId, string agentName = "Atendente", string instructions = "Responda com simpatia.", string provider = "openai", string model = "gpt-5.6-sol")
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

    /// <summary>
    /// Seed + publish + poll de um único turno — reduz repetição nos loops
    /// multi-turno acima.
    /// </summary>
    private async Task<A2ATaskRecord> RunTurnAsync(Guid agentId, string contextId, string userMessage)
    {
        var taskId = Guid.NewGuid().ToString("N");
        await SeedTaskAsync(taskId, agentId, contextId, userMessage);
        await PublishJobAsync(taskId, agentId, contextId);
        return await PollUntilTerminalAsync(taskId);
    }
}
