using System.Text.Json;
using global::A2A;
using Buteco.Workers.A2A;
using Buteco.Workers.AgentDelegations;
using Buteco.Workers.Agents;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Knowledge.Execution;
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
using Npgsql;
using RabbitMQ.Client;
using TaskStatus = A2A.TaskStatus;

namespace Buteco.Workers.Tests;

/// <summary>
/// Guardas do estado terminal quando a <b>serialização por
/// <c>(agentId, contextId)</c></b> falha — o defeito medido pela exploração
/// <c>replicas-de-worker</c>: a aquisição do <see cref="ConversationContextLock"/>
/// acontecia FORA do <c>try</c> de <c>AgentExecutionService.ExecuteAsync</c>, e
/// uma exceção ali escapava do método inteiro, deixando a task presa em
/// <c>working</c> para sempre.
///
/// <para>
/// <b>Por que estes guardas encurtam a espera pela connection string e não por
/// configuração de produção</b> (design.md, D2/D4): a espera pelo advisory lock
/// é limitada pelo <c>CommandTimeout</c> do Npgsql, 30 s por default. Não existe
/// — e deliberadamente não entra — nenhum knob de produção para esse valor,
/// porque escolhê-lo exigiria saber quanto tempo uma conversa legitimamente
/// segura o lock. Medido que <c>Command Timeout=N</c> na connection string
/// limita a espera produzindo <b>a mesma exceção</b> da produção
/// (<c>NpgsqlException</c> ⊃ <c>TimeoutException</c>), o que torna estes guardas
/// questão de segundos em vez de 30 s cada.
/// </para>
///
/// <para>
/// <b>Nenhum guarda aqui sincroniza por relógio.</b> Onde há ordem a garantir,
/// ela é ancorada em estado persistido observável (polling até a task estar
/// <c>working</c>) ou numa chamada (o <see cref="IChatClient"/> mockado, que
/// roda dentro do <c>try</c> e antes da gravação terminal) — nunca em
/// <c>Task.Delay</c> posicionado. Esta base já registra a família de suíte
/// sensível a contenção externa, e um guarda que só funciona quando a máquina
/// coopera não é guarda.
/// </para>
/// </summary>
[Collection(WorkerHostCollection.Name)]
public class ConversationContextLockFailureTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    private const string Provider = "openai";
    private const string Model = "gpt-5.6-sol";

    // Acima de qualquer comando EF normal da suíte, e bem abaixo dos 30 s de
    // produção — é o que faz o guarda medir a espera pelo lock e não latência
    // de container.
    private const string ShortCommandTimeout = "Command Timeout=3";

    // ── Guarda 1: o defeito, direto ──────────────────────────────────────
    [Fact]
    public async Task AcquisitionTimingOut_EndsTaskAsFailed_NeverLeavesItWorking()
    {
        await PurgeQueueAsync();

        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var taskId = Guid.NewGuid().ToString("N");
        await SeedAgentAsync(agentId, "Bloqueado");
        await SeedTaskAsync(taskId, agentId, contextId, "oi");

        // O lock fica em posse de uma conexão de fora da aplicação por toda a
        // duração do teste: a aquisição do worker não tem como ser satisfeita.
        await using var holder = await HoldLockAsync(agentId, contextId);

        using var host = BuildHost(SimpleChatClient(), ShortCommandTimeout);
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

    // ── Guarda 2: o caminho alcançável em produção ───────────────────────
    // Duas instâncias, duas tasks do MESMO (agentId, contextId), a primeira
    // demorando mais do que a segunda pode esperar pelo lock. É o cenário do
    // contato que manda a segunda mensagem enquanto o agente ainda responde a
    // primeira — o índice único de PendingDispatch em apps/inbox filtra por
    // Status = 'Pending', então uma linha Dispatching não impede uma nova.
    [Fact]
    public async Task TwoInstances_SameContext_SlowFirstTask_BothReachTerminalState()
    {
        await PurgeQueueAsync();

        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var firstTaskId = Guid.NewGuid().ToString("N");
        var secondTaskId = Guid.NewGuid().ToString("N");
        await SeedAgentAsync(agentId, "Lento");
        await SeedTaskAsync(firstTaskId, agentId, contextId, "primeira");
        await SeedTaskAsync(secondTaskId, agentId, contextId, "segunda");

        // Mais longo que o Command Timeout do host: enquanto a primeira roda, a
        // segunda não consegue o lock dentro do tempo disponível.
        var slowClient = SimpleChatClient(TimeSpan.FromSeconds(8));

        using var first = BuildHost(slowClient, ShortCommandTimeout);
        await first.StartAsync();

        try
        {
            await PublishJobAsync(firstTaskId, agentId, contextId);

            // Ponto de sincronização: estado persistido, não relógio. Esperar a
            // primeira task estar `working` garante que a instância que a pegou
            // está ocupada (prefetchCount: 1, mensagem não confirmada) ANTES de
            // a segunda instância existir — sem isso, as duas tasks poderiam
            // cair na mesma instância, serializar sem disputa de lock nenhuma, e
            // o guarda passaria verde sem exercitar o defeito.
            await PollUntilStateAsync(firstTaskId, nameof(TaskState.Working));

            using var second = BuildHost(slowClient, ShortCommandTimeout);
            await second.StartAsync();

            try
            {
                await PublishJobAsync(secondTaskId, agentId, contextId);

                var firstRecord = await PollUntilTerminalAsync(firstTaskId);
                var secondRecord = await PollUntilTerminalAsync(secondTaskId);

                // Afirmar "as duas terminam", não qual terminou como quê: quem
                // ganha o lock é o RabbitMQ que decide. O que o defeito produzia
                // era uma delas parada em `working` para sempre.
                Assert.Equal(
                    [nameof(TaskState.Completed), nameof(TaskState.Failed)],
                    new[] { firstRecord.State, secondRecord.State }.Order().ToArray());
            }
            finally
            {
                await second.StopAsync();
            }
        }
        finally
        {
            await first.StopAsync();
        }
    }

    // ── Guarda 3: prende a alternativa recusada em D1 ────────────────────
    // Se alguém mover o `await using var contextLock` para dentro do `try`, o
    // DisposeAsync passa a ser coberto pelo catch — e uma falha ao liberar o
    // lock, DEPOIS de a task já estar `completed`, a regrava como `failed`
    // (ApplyStepAsync não tem guarda de estado terminal). Este guarda PASSA em
    // HEAD de propósito: ele é de regressão contra a correção errada, não
    // contra o defeito corrigido.
    [Fact]
    public async Task UnlockFailingAfterCompletion_LeavesTaskCompleted_WithArtifactPreserved()
    {
        await PurgeQueueAsync();

        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        var taskId = Guid.NewGuid().ToString("N");
        await SeedAgentAsync(agentId, "Solta Mal");
        await SeedTaskAsync(taskId, agentId, contextId, "oi");

        const string expectedAnswer = "Resposta que não pode ser perdida.";

        // A derrubada roda DE DENTRO do IChatClient mockado. A ordenação é da
        // sequência de chamadas, não de tempo: RunAsync (e portanto este mock)
        // roda dentro do try; a gravação terminal vem depois dele; e o
        // DisposeAsync do lock vem depois de tudo. Quando este delegate retorna,
        // o backend dono do lock já está morto.
        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await TerminateLockBackendAsync(agentId, contextId);
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, expectedAnswer));
            });

        using var host = BuildHost(chatClient.Object, ShortCommandTimeout);
        await host.StartAsync();

        try
        {
            await PublishJobAsync(taskId, agentId, contextId);

            var record = await PollUntilTerminalAsync(taskId);
            Assert.Equal(nameof(TaskState.Completed), record.State);
            Assert.Equal(expectedAnswer, ExtractArtifactText(record));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    // ── Guarda 4: o escopo descartado na falha de aquisição ──────────────
    // Afirma CONSEQUÊNCIA, não implementação. Sem o descarte, cada aquisição
    // que falha pendura um IServiceScope com uma conexão Postgres já aberta;
    // com o pool capado, ele esgota e o worker não consegue nem gravar as
    // próprias falhas — as tasks voltam a ficar presas em `working`, que é o
    // defeito original entrando por outra porta.
    //
    // O teto é BAIXADO pela connection string em vez de o número de iterações
    // ser elevado acima do default de 100 (design.md, D9): a forma recusada são
    // ~5 min de suíte, esta são segundos. Contar `pg_stat_activity` não serve —
    // medido que conexão devolvida ao pool é backend vivo exatamente como uma
    // vazada, então a contagem não distingue as duas.
    [Fact]
    public async Task RepeatedAcquisitionFailures_DoNotExhaustTheConnectionPool()
    {
        await PurgeQueueAsync();

        const int poolSize = 3;
        const int taskCount = 5;

        var agentId = Guid.NewGuid();
        var contextId = Guid.NewGuid().ToString("N");
        await SeedAgentAsync(agentId, "Vaza");

        var taskIds = new List<string>();
        for (var i = 0; i < taskCount; i++)
        {
            var taskId = Guid.NewGuid().ToString("N");
            await SeedTaskAsync(taskId, agentId, contextId, $"mensagem {i}");
            taskIds.Add(taskId);
        }

        await using var holder = await HoldLockAsync(agentId, contextId);

        // Timeout=5 é o tempo de ESPERA POR CONEXÃO do pool (não o de comando):
        // sem ele, uma tentativa contra pool esgotado ficaria 15 s parada.
        using var host = BuildHost(
            SimpleChatClient(), $"{ShortCommandTimeout};Maximum Pool Size={poolSize};Timeout=5");
        await host.StartAsync();

        try
        {
            foreach (var taskId in taskIds)
            {
                await PublishJobAsync(taskId, agentId, contextId);
            }

            foreach (var taskId in taskIds)
            {
                var record = await PollUntilTerminalAsync(taskId, maxAttempts: 300);
                Assert.Equal(nameof(TaskState.Failed), record.State);
            }
        }
        finally
        {
            await host.StopAsync();
        }
    }

    // ── infraestrutura do teste ──────────────────────────────────────────

    private static IChatClient SimpleChatClient(TimeSpan? delay = null)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                if (delay is not null)
                {
                    await Task.Delay(delay.Value);
                }

                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
            });
        return mock.Object;
    }

    /// <summary>
    /// Segura o mesmo lock que <see cref="ConversationContextLock"/> pediria,
    /// numa conexão fora do pool da aplicação — <c>Pooling=false</c> para o
    /// backend ser exclusivo desta posse e morrer junto com ela.
    /// </summary>
    private async Task<NpgsqlConnection> HoldLockAsync(Guid agentId, string contextId)
    {
        var connection = new NpgsqlConnection($"{fixture.Postgres.GetConnectionString()};Pooling=false");
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(hashtext(@a), hashtext(@c))", connection);
        command.Parameters.AddWithValue("a", agentId.ToString());
        command.Parameters.AddWithValue("c", contextId);
        await command.ExecuteNonQueryAsync();

        return connection;
    }

    /// <summary>
    /// Derruba o backend que segura o lock deste par, localizado pelo PRÓPRIO
    /// par de chaves — nunca por pid adivinhado.
    /// </summary>
    /// <remarks>
    /// <c>objsubid</c> distingue as duas formas de lock consultivo, e o valor
    /// foi conferido contra o Postgres: <c>pg_advisory_lock(int4, int4)</c>
    /// grava <c>objsubid = 2</c>, com uma chave em cada coluna;
    /// <c>pg_advisory_lock(bigint)</c> grava <c>objsubid = 1</c>, com as
    /// METADES do mesmo número em <c>classid</c>/<c>objid</c>. O sistema só usa
    /// a forma de duas chaves hoje, então o filtro não muda o resultado — ele
    /// existe para este guarda continuar derrubando o backend certo se a outra
    /// forma aparecer depois. Não é precaução teórica: na conferência, um lock
    /// de chave única qualquer apareceu com <c>classid = 2</c>, que um par de
    /// duas chaves pode ter por coincidência.
    /// </remarks>
    private async Task TerminateLockBackendAsync(Guid agentId, string contextId)
    {
        await using var connection = new NpgsqlConnection($"{fixture.Postgres.GetConnectionString()};Pooling=false");
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT pg_terminate_backend(l.pid)
            FROM pg_locks l
            WHERE l.locktype = 'advisory'
              AND l.objsubid = 2
              AND l.classid  = (hashtext(@a)::bigint & 4294967295)
              AND l.objid    = (hashtext(@c)::bigint & 4294967295)
            """, connection);
        command.Parameters.AddWithValue("a", agentId.ToString());
        command.Parameters.AddWithValue("c", contextId);

        var terminated = 0;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            terminated++;
        }

        // Se nada foi derrubado, o guarda não exercitou nada e passaria verde
        // sem significar coisa alguma.
        Assert.Equal(1, terminated);
    }

    private IHost BuildHost(IChatClient chatClient, string connectionStringSuffix)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration["ConnectionStrings:Postgres"] =
            $"{fixture.Postgres.GetConnectionString()};{connectionStringSuffix}";
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
        builder.Services.AddSingleton<IKnowledgeToolSetResolver, NullKnowledgeToolSetResolver>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHttpClient(PushNotificationSender.HttpClientName)
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));
        builder.Services.AddSingleton<PushNotificationSender>();
        builder.Services.AddSingleton<ToolNameDeduplicator>();
        builder.Services.AddSingleton<AgentExecutionService>();
        builder.Services.AddHostedService<TaskJobConsumer>();

        return builder.Build();
    }

    private async Task SeedAgentAsync(Guid agentId, string agentName)
    {
        await using var dbContext = CreateDbContext();

        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "IsActive", "Provider", "Model", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {agentName}, {"Instruções de teste."}, {true}, {Provider}, {Model}, {now}, {now})
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

    private ConnectionFactory RabbitMqFactory() => new()
    {
        HostName = fixture.RabbitMq.Hostname,
        Port = fixture.RabbitMq.GetMappedPublicPort(5672),
        UserName = "buteco",
        Password = "buteco_test_password",
    };

    /// <summary>
    /// Esvazia a fila antes de cada guarda. Um teste que deixe mensagem não
    /// confirmada (é o que o defeito produz) a devolve para a fila ao fechar a
    /// conexão, e ela seria consumida pelo host do guarda seguinte.
    /// </summary>
    private async Task PurgeQueueAsync()
    {
        await using var connection = await RabbitMqFactory().CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(TaskJobConsumer.QueueName, durable: true, exclusive: false, autoDelete: false);
        await channel.QueuePurgeAsync(TaskJobConsumer.QueueName);
    }

    private async Task PublishJobAsync(string taskId, Guid agentId, string contextId)
    {
        await using var connection = await RabbitMqFactory().CreateConnectionAsync();
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

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()).Options);

    private static string? ExtractArtifactText(A2ATaskRecord record)
    {
        var task = JsonSerializer.Deserialize<AgentTask>(record.Payload, A2AJsonUtilities.DefaultOptions)!;
        return task.Artifacts?.LastOrDefault()?.Parts.FirstOrDefault(part => part.Text is not null)?.Text;
    }

    private async Task<A2ATaskRecord> PollUntilStateAsync(string taskId, string state, int maxAttempts = 150)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            await using var dbContext = CreateDbContext();
            var record = await dbContext.A2ATasks.AsNoTracking().FirstOrDefaultAsync(task => task.TaskId == taskId);
            if (record is not null && record.State == state)
            {
                return record;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Task '{taskId}' não chegou ao estado '{state}' a tempo.");
    }

    private async Task<A2ATaskRecord> PollUntilTerminalAsync(string taskId, int maxAttempts = 150)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            await using var dbContext = CreateDbContext();
            var record = await dbContext.A2ATasks.AsNoTracking().FirstOrDefaultAsync(task => task.TaskId == taskId);

            if (record is not null
                && record.State is nameof(TaskState.Completed) or nameof(TaskState.Failed) or nameof(TaskState.Rejected))
            {
                return record;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException(
            $"Task '{taskId}' não atingiu um estado terminal a tempo — é o sintoma do defeito: presa em `working`.");
    }
}
