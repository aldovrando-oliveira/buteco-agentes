using global::A2A;
using Buteco.Workers.Messaging;
using Buteco.Workers.Tests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Moq;
using RabbitMQ.Client;

namespace Buteco.Workers.Tests;

/// <summary>
/// Guardas 1, 3 e 4 da change workers-consumo-concorrente (#163, D7): várias tasks
/// no mesmo host, com <c>TaskConsumer:MaxConcurrentExecutions = 2</c>.
/// </summary>
/// <remarks>
/// <b>Parte de <c>TaskJobConsumerTests</c>, e não classe própria, de propósito.</b>
/// Uma classe nova com host seria a 15ª da coleção <c>worker-hosts</c>: par próprio de
/// containers e recalibração obrigatória da referência de duração da suíte
/// (<c>WorkerHostCollection</c>, convenção 22). Como <c>partial</c>, os guardas usam a
/// mesma fixture, os mesmos builders e os mesmos helpers, num arquivo separado.
/// </remarks>
public partial class TaskJobConsumerTests
{
    /// <summary>
    /// <b>Guarda 1 — o que prende a pegadinha do construtor de
    /// <c>CreateChannelOptions</c></b> (D1). Com <c>N = 2</c> e três tasks que não
    /// terminam, exatamente duas ficam em <c>Working</c> e a terceira continua
    /// <b>pronta na fila</b>, ao alcance de outra instância.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reprova nos dois sentidos em que prefetch e concorrência divergem.
    /// <b>Concorrência 1</b> (o argumento <c>consumerDispatchConcurrency</c> omitido, ou
    /// o valor movido para a <c>ConnectionFactory</c>: o construtor público fixa 1 por
    /// cima dela): só uma em <c>Working</c>. <b>Prefetch maior que N</b>: a terceira sai
    /// da fila e fica presa no processo sem executar, <c>messages_ready == 0</c> — é
    /// onde a task do Target da delegação ficaria parada.
    /// </para>
    /// <para>
    /// <b>QUEM APAGAR OU AFROUXAR ESTE GUARDA REABRE O DEFEITO SEM NENHUM OUTRO TESTE
    /// PERCEBER.</b> A asserção é o retrato inteiro, depois de uma folga, e não "chegou
    /// a duas": com prefetch 3 a fila esvazia logo depois da entrega, e um retrato
    /// tirado cedo poderia ver <c>1</c> pronta por acaso.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TwoSlots_ThreeSlowTasks_ExactlyTwoWorking_AndTheThirdStaysReadyInTheQueue()
    {
        await DeclareAndPurgeTaskQueueAsync();

        var agentId = Guid.NewGuid();
        await SeedAgentAsync(agentId, "Atendente", "Responda com simpatia.");

        // Contextos diferentes: com o mesmo, o lock de contexto serializaria as três
        // e o guarda mediria o lock, não os slots.
        var tasks = new List<(string TaskId, string ContextId)>();
        for (var i = 0; i < 3; i++)
        {
            var task = (Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
            await SeedTaskAsync(task.Item1, agentId, task.Item2, $"lenta {i}");
            tasks.Add(task);
        }

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(async (IEnumerable<ChatMessage> _, ChatOptions? _, CancellationToken cancellationToken) =>
            {
                await release.Task.WaitAsync(cancellationToken);
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
            });

        using var host = BuildHost(chatClient.Object, maxConcurrentExecutions: 2);
        await host.StartAsync();

        try
        {
            foreach (var (taskId, contextId) in tasks)
            {
                await PublishJobAsync(taskId, agentId, contextId);
            }

            // Espera chegar a duas em Working (sem lançar: com concorrência 1 nunca
            // chega, e o retrato abaixo é o que mostra o defeito), e então uma folga
            // para o broker terminar de entregar o que o prefetch deixar.
            for (var attempt = 0; attempt < 75 && await CountInStateAsync(tasks, nameof(TaskState.Working)) < 2; attempt++)
            {
                await Task.Delay(200);
            }

            await Task.Delay(TimeSpan.FromSeconds(2));

            var snapshot = (
                Working: await CountInStateAsync(tasks, nameof(TaskState.Working)),
                Submitted: await CountInStateAsync(tasks, nameof(TaskState.Submitted)),
                Ready: await ReadyTaskMessagesAsync());
            output.WriteLine($"retrato com N = 2: working = {snapshot.Working}, submitted = {snapshot.Submitted}, prontas na fila = {snapshot.Ready}");

            Assert.Equal((2, 1, 1u), snapshot);
        }
        finally
        {
            release.TrySetResult();
            foreach (var (taskId, _) in tasks)
            {
                await PollUntilTerminalAsync(taskId);
            }

            await host.StopAsync();
            await DeclareAndPurgeTaskQueueAsync();
        }
    }

    /// <summary>
    /// <b>Guarda 3 — parada com várias execuções em voo</b> (D5; spec
    /// a2a-task-lifecycle, "Parada com várias execuções em voo cabe no prazo do
    /// compose"). <c>N = 2</c>, duas execuções esperando o provedor pelo token: a
    /// parada volta em menos de 10 s, as duas tasks ficam em <c>Working</c>, os dois
    /// jobs voltam à fila e nenhum lock de contexto continua em posse.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Os locks são lidos nas duas pontas, como no guarda 2.5 da parada: <c>2</c> com
    /// as execuções em voo (sem isso o <c>0</c> depois prova ausência, não liberação)
    /// e <c>0</c> depois da parada.
    /// </para>
    /// <para>
    /// <b>O que ele pega, medido por mutação:</b> a ordem antiga da parada (fechar o
    /// canal antes de <c>base.StopAsync</c>, a #49) leva 30,4 s e reprova. <b>O que ele
    /// NÃO pega:</b> o <c>nack</c> da parada removido ou com <c>requeue: false</c>, porque
    /// quem devolve os jobs é o fechamento do canal (#177). "Dois jobs na fila" prova o
    /// resultado, não o caminho.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task StopAsync_WithTwoExecutionsInFlight_ReturnsPromptly_RequeuesBothJobs_AndReleasesBothLocks()
    {
        await DeclareAndPurgeTaskQueueAsync();

        var agentId = Guid.NewGuid();
        await SeedAgentAsync(agentId, "Atendente", "Responda com simpatia.");
        var tasks = new List<(string TaskId, string ContextId)>();
        for (var i = 0; i < 2; i++)
        {
            var task = (Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
            await SeedTaskAsync(task.Item1, agentId, task.Item2, $"em voo {i}");
            tasks.Add(task);
        }

        var entered = 0;
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(async (IEnumerable<ChatMessage> _, ChatOptions? _, CancellationToken cancellationToken) =>
            {
                if (Interlocked.Increment(ref entered) == 2)
                {
                    bothEntered.TrySetResult();
                }

                await Task.Delay(Timeout.Infinite, cancellationToken);
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "nunca"));
            });

        var stopwatch = new System.Diagnostics.Stopwatch();
        Exception? stopFailure = null;
        long locksWhileInFlight;

        using (var host = BuildHost(chatClient.Object, maxConcurrentExecutions: 2))
        {
            await host.StartAsync();
            foreach (var (taskId, contextId) in tasks)
            {
                await PublishJobAsync(taskId, agentId, contextId);
            }

            // Sem lançar: com um slot só a segunda nunca entra, e o retrato abaixo
            // mostra isso junto do resto.
            await Task.WhenAny(bothEntered.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            locksWhileInFlight = await CountLocksAsync(agentId, tasks);

            stopwatch.Start();
            try
            {
                await host.StopAsync();
            }
            catch (Exception ex)
            {
                stopFailure = ex;
            }

            stopwatch.Stop();
        }

        var snapshot = (
            Entered: Volatile.Read(ref entered),
            LocksWhileInFlight: locksWhileInFlight,
            Working: await CountInStateAsync(tasks, nameof(TaskState.Working)),
            Ready: await ReadyTaskMessagesAsync(),
            LocksAfterStop: await CountLocksAsync(agentId, tasks));
        output.WriteLine(
            $"parada com N = 2: {stopwatch.Elapsed.TotalSeconds:F1} s, exceção = {stopFailure?.GetType().Name ?? "nenhuma"}, " +
            $"entraram = {snapshot.Entered}, locks em voo/depois = {snapshot.LocksWhileInFlight}/{snapshot.LocksAfterStop}, " +
            $"working = {snapshot.Working}, prontas na fila = {snapshot.Ready}");

        await DeclareAndPurgeTaskQueueAsync();

        Assert.Null(stopFailure);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"StopAsync levou {stopwatch.Elapsed.TotalSeconds:F1} s com duas execuções em voo.");
        Assert.Equal((2, 2L, 2, 2u, 0L), snapshot);
    }

    /// <summary>
    /// <b>Guarda 4 — <c>AsyncLocal</c> sem contaminação no mesmo host</b> (spec
    /// agent-execution-metrics, "Duas execuções no mesmo host"). <c>N = 2</c>, duas
    /// tasks de agentes com provedores diferentes, <b>simultâneas</b>: cada linha de
    /// <c>provider_calls</c> tem o provedor do agente da própria task.
    /// </summary>
    /// <remarks>
    /// <para>
    /// As duas chamadas ao provedor se encontram numa barreira antes de responder, e o
    /// pico de chamadas simultâneas é afirmado: sem ele, duas execuções em série
    /// passariam neste guarda sem ter sido concorrentes. A barreira tem prazo, para que
    /// o código com um slot só termine e o pico (<c>1</c>) apareça na falha.
    /// </para>
    /// <para>
    /// Reprova se <c>ExecutionMetricsScope</c> deixar de ser <c>AsyncLocal</c>: com um
    /// campo compartilhado, a chamada de uma task é gravada no escopo da outra. É o
    /// mesmo risco que <c>ExecutionMetricsScopeTests.TwoConcurrentScopes_DoNotMixTheirCalls</c>
    /// guarda no nível unitário, aqui no caminho de produção desta change.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TwoSlots_SameHost_TwoProviders_EachProviderCallBelongsToItsOwnTask()
    {
        await DeclareAndPurgeTaskQueueAsync();

        var openAiAgentId = Guid.NewGuid();
        var anthropicAgentId = Guid.NewGuid();
        await SeedAgentAsync(openAiAgentId, "Atendente", "Responda com simpatia.", "openai", "gpt-5.6-sol");
        await SeedAgentAsync(anthropicAgentId, "Financeiro", "Responda com simpatia.", "anthropic", "claude-opus-5");

        var openAiTask = (TaskId: Guid.NewGuid().ToString("N"), ContextId: Guid.NewGuid().ToString("N"));
        var anthropicTask = (TaskId: Guid.NewGuid().ToString("N"), ContextId: Guid.NewGuid().ToString("N"));
        await SeedTaskAsync(openAiTask.TaskId, openAiAgentId, openAiTask.ContextId, "pergunta A");
        await SeedTaskAsync(anthropicTask.TaskId, anthropicAgentId, anthropicTask.ContextId, "pergunta B");

        var inFlight = 0;
        var peak = 0;
        var bothInFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(async (IEnumerable<ChatMessage> _, ChatOptions? _, CancellationToken cancellationToken) =>
            {
                var now = Interlocked.Increment(ref inFlight);
                InterlockedMax(ref peak, now);
                if (now == 2)
                {
                    bothInFlight.TrySetResult();
                }

                try
                {
                    await Task.WhenAny(bothInFlight.Task, Task.Delay(TimeSpan.FromSeconds(3), cancellationToken));
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
                    {
                        Usage = new UsageDetails { InputTokenCount = 1, OutputTokenCount = 1 },
                    };
                }
                finally
                {
                    Interlocked.Decrement(ref inFlight);
                }
            });

        using var host = BuildMeasuredHost(chatClient.Object, maxConcurrentExecutions: 2);
        await host.StartAsync();

        try
        {
            await PublishJobAsync(openAiTask.TaskId, openAiAgentId, openAiTask.ContextId);
            await PublishJobAsync(anthropicTask.TaskId, anthropicAgentId, anthropicTask.ContextId);
            Assert.Equal(nameof(TaskState.Completed), (await PollUntilTerminalAsync(openAiTask.TaskId)).State);
            Assert.Equal(nameof(TaskState.Completed), (await PollUntilTerminalAsync(anthropicTask.TaskId)).State);
            await ExecutionMetricsReader.WaitForClosedExecutionAsync(ConnectionString, openAiTask.TaskId);
            await ExecutionMetricsReader.WaitForClosedExecutionAsync(ConnectionString, anthropicTask.TaskId);
        }
        finally
        {
            await host.StopAsync();
        }

        var openAiCalls = await ExecutionMetricsReader.ProviderCallsAsync(ConnectionString, openAiTask.TaskId);
        var anthropicCalls = await ExecutionMetricsReader.ProviderCallsAsync(ConnectionString, anthropicTask.TaskId);
        output.WriteLine(
            $"pico de chamadas simultâneas = {Volatile.Read(ref peak)}; " +
            $"openai: [{string.Join(", ", openAiCalls.Select(c => c.Provider))}]; anthropic: [{string.Join(", ", anthropicCalls.Select(c => c.Provider))}]");

        Assert.Equal(2, Volatile.Read(ref peak));
        Assert.Equal(["openai"], openAiCalls.Select(call => call.Provider));
        Assert.Equal(["anthropic"], anthropicCalls.Select(call => call.Provider));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    private async Task<int> CountInStateAsync(IEnumerable<(string TaskId, string ContextId)> tasks, string state)
    {
        var count = 0;
        foreach (var (taskId, _) in tasks)
        {
            if (await ReadTaskStateAsync(taskId) == state)
            {
                count++;
            }
        }

        return count;
    }

    private async Task<long> CountLocksAsync(Guid agentId, IEnumerable<(string TaskId, string ContextId)> tasks)
    {
        long count = 0;
        foreach (var (_, contextId) in tasks)
        {
            count += await CountContextAdvisoryLocksAsync(agentId, contextId);
        }

        return count;
    }

    // Declara antes de esvaziar: rodando sozinho, nenhum host subiu ainda e a fila
    // não existe (PurgeTaskQueueAsync responderia 404).
    private async Task DeclareAndPurgeTaskQueueAsync()
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
        await channel.QueuePurgeAsync(TaskJobConsumer.QueueName);
    }

    // messages_ready: o message_count do queue.declare-ok conta só as prontas, não
    // as entregues e ainda não confirmadas — é exatamente a distinção do guarda 1.
    private async Task<uint> ReadyTaskMessagesAsync()
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
        return (await channel.QueueDeclarePassiveAsync(TaskJobConsumer.QueueName)).MessageCount;
    }
}
