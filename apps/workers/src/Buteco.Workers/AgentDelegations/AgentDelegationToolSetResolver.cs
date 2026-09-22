using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using A2A;
using Buteco.Workers.A2A;
using Buteco.Workers.Agents;
using Buteco.Workers.Agents.Entities;
using Buteco.Workers.ExecutionMetrics;
using Buteco.Workers.ExecutionMetrics.Entities;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Mcp;
using Buteco.Workers.Naming;
using Buteco.Workers.Messaging;
using Buteco.Workers.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Buteco.Workers.AgentDelegations;

public sealed class AgentDelegationToolSetResolver(
    IServiceScopeFactory scopeFactory,
    ITaskJobPublisher taskJobPublisher,
    IOptions<AgentDelegationToolOptions> options,
    ILogger<AgentDelegationToolSetResolver> logger) : IAgentDelegationToolSetResolver
{
    private const string ToolNamePrefix = "delegate_to_";

    public async Task<IReadOnlyList<AITool>> ResolveAsync(
        AppDbContext dbContext,
        Agent sourceAgent,
        string sourceTaskId,
        string contextId,
        int currentDepth,
        DateTimeOffset? messageInstant,
        CancellationToken cancellationToken)
    {
        var delegations = await (
            from delegation in dbContext.AgentDelegations.AsNoTracking()
            join target in dbContext.Agents.AsNoTracking() on delegation.TargetAgentId equals target.Id
            where delegation.SourceAgentId == sourceAgent.Id
            orderby delegation.TargetAgentId
            select new { delegation.TargetAgentId, TargetName = target.Name }
        ).ToListAsync(cancellationToken);

        // Sem dedupe local: quem garante unicidade é ToolNameDeduplicator, no
        // ponto que une este conjunto ao das tools MCP (design.md da change
        // dedupe-global-nome-de-tool, Decisão 3). Manter os dois seria dois
        // mecanismos para a mesma invariante — e o local era o que tinha o
        // defeito de estourar os 64 ao concatenar o sufixo sem re-truncar.
        // O `orderby delegation.TargetAgentId` acima é o que dá determinismo a
        // este lado, e continua sendo necessário: é dele que sai a ordem em que
        // o deduplicador desempata.
        var tools = new List<AITool>(delegations.Count);

        foreach (var delegation in delegations)
        {
            var toolName = ToolNameSanitizer.Sanitize($"{ToolNamePrefix}{ToolNameSlugifier.Slugify(delegation.TargetName)}");
            tools.Add(BuildDelegationTool(toolName, delegation.TargetAgentId, delegation.TargetName, sourceAgent.Id, sourceTaskId, contextId, currentDepth, messageInstant));
        }

        return tools;
    }

    private AITool BuildDelegationTool(
        string toolName, Guid targetAgentId, string targetAgentName, Guid sourceAgentId, string sourceTaskId, string contextId, int currentDepth, DateTimeOffset? messageInstant)
    {
        async Task<string> DelegateAsync(
            [Description("A tarefa ou pergunta a delegar para o agente Target.")] string message,
            CancellationToken cancellationToken) =>
            await DelegateToTargetAsync(targetAgentId, sourceAgentId, sourceTaskId, contextId, currentDepth, messageInstant, message, cancellationToken);

        return AIFunctionFactory.Create(
            (Func<string, CancellationToken, Task<string>>)DelegateAsync,
            name: toolName,
            description: $"Delega esta tarefa para o agente '{targetAgentName}'. Aguarda a conclusão e retorna o resultado produzido por ele.");
    }

    private async Task<string> DelegateToTargetAsync(
        Guid targetAgentId,
        Guid sourceAgentId,
        string sourceTaskId,
        string contextId,
        int currentDepth,
        DateTimeOffset? messageInstant,
        string message,
        CancellationToken cancellationToken)
    {
        // Cada saída deste método registra um resultado de delegação (change
        // metricas-execucao-coleta, D5) — as três de "não iniciada" aqui, as
        // três com task criada em WaitForTerminalStateAsync. O texto devolvido
        // ao LLM e os logs NÃO mudam: a linha é o gêmeo durável deles.
        var started = Stopwatch.GetTimestamp();

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Decision 5 (revisado durante a implementação — ver nota no
        // design.md): Source também lido fresco, mesmo padrão do Target
        // logo abaixo. O `Agent` carregado no início de
        // AgentExecutionService.ExecuteAsync é um snapshot fixo da mesma
        // instância em memória — reaproveitá-lo (como o design original
        // prescrevia, "sem nova query") nunca pegaria uma desativação
        // ocorrida DEPOIS desse carregamento e ANTES desta chamada, que é
        // exatamente o cenário "Source desativado durante o
        // processamento" que este código existe para cobrir.
        var source = await dbContext.Agents.AsNoTracking().FirstOrDefaultAsync(agent => agent.Id == sourceAgentId, cancellationToken);
        if (source is null || !source.IsActive || source.Provider is null || source.Model is null)
        {
            logger.LogWarning(
                "Delegação do agente {SourceAgentId} para {TargetAgentId} recusada: Source não está mais ativo/configurado.",
                sourceAgentId,
                targetAgentId);
            RecordNotStarted(sourceTaskId, sourceAgentId, targetAgentId, started);
            return "Delegação não realizada: o agente que está delegando não está mais ativo ou configurado.";
        }

        // Decision 4: Target lido fresco — AsNoTracking força uma consulta
        // nova ao banco a cada chamada, nunca reaproveita estado em memória.
        var target = await dbContext.Agents.AsNoTracking().FirstOrDefaultAsync(agent => agent.Id == targetAgentId, cancellationToken);
        if (target is null || !target.IsActive || target.Provider is null || target.Model is null)
        {
            logger.LogWarning(
                "Delegação do agente {SourceAgentId} para {TargetAgentId} recusada: Target inativo, ausente ou sem Provider/Model configurados.",
                sourceAgentId,
                targetAgentId);
            RecordNotStarted(sourceTaskId, sourceAgentId, targetAgentId, started);
            return "Delegação não realizada: o agente Target não está disponível no momento.";
        }

        var targetTaskId = Guid.NewGuid().ToString("N");
        var targetTaskStore = new PostgresTaskStore(scopeFactory, targetAgentId);

        var targetTask = await CreateDelegatedTaskAsync(
            targetTaskStore, targetTaskId, contextId, currentDepth + 1, sourceAgentId, sourceTaskId, messageInstant, message, cancellationToken);
        if (targetTask is null)
        {
            logger.LogError("Falha ao criar a task delegada {TargetTaskId} para o agente {TargetAgentId}.", targetTaskId, targetAgentId);
            RecordNotStarted(sourceTaskId, sourceAgentId, targetAgentId, started);
            return "Delegação não realizada: falha interna ao criar a task do agente Target.";
        }

        await taskJobPublisher.PublishAsync(new TaskJobMessage(targetTaskId, targetAgentId, contextId), cancellationToken);

        return await WaitForTerminalStateAsync(targetTaskStore, targetTaskId, targetAgentId, sourceAgentId, sourceTaskId, started, cancellationToken);
    }

    private static void RecordNotStarted(string sourceTaskId, Guid sourceAgentId, Guid targetAgentId, long started) =>
        ExecutionMetricsScope.RecordDelegation(new DelegationOutcome(
            sourceTaskId,
            sourceAgentId,
            targetAgentId,
            targetTaskId: null,
            ExecutionMetricsValues.DelegationOutcome.NotStarted,
            lastObservedTargetState: null,
            lastObservedAt: null,
            successfulReadCount: 0,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds));

    private static void RecordWithTargetTask(
        string sourceTaskId,
        Guid sourceAgentId,
        Guid targetAgentId,
        string targetTaskId,
        string outcome,
        TaskState? lastObservedState,
        DateTimeOffset? lastObservationAt,
        int successfulReadCount,
        long started) =>
        ExecutionMetricsScope.RecordDelegation(new DelegationOutcome(
            sourceTaskId,
            sourceAgentId,
            targetAgentId,
            targetTaskId,
            outcome,
            lastObservedState?.ToString(),
            lastObservationAt,
            successfulReadCount,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds));

    /// <summary>
    /// Cria a task `submitted` do Target com a mensagem delegada em
    /// `History` e o contador de profundidade já em `Metadata` — mesma
    /// mecânica de fila (`AgentEventQueue`/`TaskUpdater`/`TaskProjection`)
    /// já usada por `AgentExecutionService.ApplyStepAsync`, mas gravando
    /// `Metadata` antes do primeiro `SaveTaskAsync` (Decision 6), não só na
    /// conclusão.
    /// </summary>
    /// <remarks>
    /// Quando <paramref name="messageInstant"/> não é nulo, grava-o em
    /// <c>Message.Metadata[MessageInstantCodec.MetadataKey]</c> do próprio
    /// `Message` que constrói para o Target — não num transporte separado.
    /// O Target lê esse valor pelo mesmo mecanismo que qualquer task usa
    /// (`AgentExecutionService.ExtractMessageInstant`, filtrando por
    /// `Role.User` sobre `History`), porque este `Message` satisfaz o mesmo
    /// filtro (invariante verificado contra o código real, não assumido —
    /// design.md da change inbox-instante-mensagem, Decisão D3).
    /// </remarks>
    private static async Task<AgentTask?> CreateDelegatedTaskAsync(
        PostgresTaskStore targetTaskStore,
        string targetTaskId,
        string contextId,
        int childDepth,
        Guid sourceAgentId,
        string sourceTaskId,
        DateTimeOffset? messageInstant,
        string message,
        CancellationToken cancellationToken)
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, targetTaskId, contextId);

        await updater.SubmitAsync(cancellationToken);
        await queue.EnqueueMessageAsync(
            new Message
            {
                Role = Role.User,
                Parts = [Part.FromText(message)],
                MessageId = Guid.NewGuid().ToString("N"),
                ContextId = contextId,
                Metadata = messageInstant is not null
                    ? new Dictionary<string, JsonElement> { [MessageInstantCodec.MetadataKey] = MessageInstantCodec.Encode(messageInstant.Value) }
                    : null,
            },
            cancellationToken);
        queue.Complete();

        AgentTask? task = null;
        await foreach (var streamEvent in queue.WithCancellation(cancellationToken))
        {
            task = TaskProjection.Apply(task, streamEvent);
        }

        if (task is null)
        {
            return null;
        }

        task.Metadata = new Dictionary<string, JsonElement>
        {
            [DelegationDepth.MetadataKey] = DelegationDepth.Encode(childDepth),
        };

        // A origem nasce junto da profundidade e pelo mesmo motivo: é o worker
        // do alvo que precisa dela, na mesma linha do tempo de fila (ver
        // DelegationOrigin).
        DelegationOrigin.Write(task.Metadata, sourceAgentId, sourceTaskId);

        await targetTaskStore.SaveTaskAsync(targetTaskId, task, cancellationToken);
        return task;
    }

    /// <summary>
    /// Aguarda a task do Target chegar a um estado terminal e, em qualquer dos
    /// dois caminhos de falha, registra o diagnóstico da desistência.
    /// </summary>
    /// <remarks>
    /// <b>POR QUE O CAMPO SE CHAMA "ÚLTIMO ESTADO OBSERVADO", E NÃO "ESTADO NA
    /// DESISTÊNCIA".</b> O estado que separa as causas é lido dentro do laço,
    /// e o ponto que registra é o <c>catch</c> — antes desta change a variável
    /// do laço estava declarada dentro dele e simplesmente não existia ali.
    /// Hoistá-la resolve o alcance, mas não torna a leitura simultânea à
    /// desistência: entre a última leitura bem-sucedida e o cancelamento cabe
    /// um <c>PollInterval</c> inteiro mais a chamada que foi cancelada. Chamar
    /// o campo de "estado na desistência" afirmaria mais do que o sistema sabe
    /// (convenção 13), num registro cujo propósito é ser lido como evidência.
    /// Uma releitura fresca dentro do <c>catch</c> foi recusada (design.md, D1):
    /// é ida ao banco num caminho de degradação graciosa, que por convenção 4
    /// não pode lançar, e compraria ≤ 1 s de precisão sobre uma janela de 120 s
    /// numa distinção que é <b>estrutural</b>, não temporal — uma task que virou
    /// `Working` no último segundo passou os outros 119 s sem ser consumida.
    ///
    /// <para>
    /// <b>QUATRO ORIGENS, NÃO DUAS, e é o <c>SuccessfulReadCount</c> que as
    /// separa.</b> `Submitted` e `Working` são estados <b>lidos</b>. Ausência de
    /// estado com contagem de leituras <b>zero</b> é "não sei" — a espera foi
    /// cancelada antes de qualquer leitura concluir. Ausência de estado com
    /// contagem <b>maior que zero</b> é "sei que a linha não estava lá" — a
    /// leitura funcionou e o store não tinha a task. Colapsar os dois últimos
    /// num único texto gastaria o vocabulário de "não sei" num fato conhecido.
    /// </para>
    ///
    /// <para>
    /// <b>O `Failed` QUE CHEGA AQUI É INDISTINGUÍVEL POR CONSTRUÇÃO, e é por
    /// isso que o registro carrega identificadores em vez de veredito.</b> Os
    /// dois caminhos de falha de <c>AgentExecutionService</c> — a aquisição do
    /// <c>ConversationContextLock</c> que estoura (change
    /// lock-de-contexto-falha-terminal) e o erro de provedor — chamam o mesmo
    /// <c>FailTaskAsync</c>, que chama <c>TaskUpdater.FailAsync</c> <b>sem
    /// mensagem</b> (verificado por decompilação do A2A 1.0.0-preview2: a
    /// assinatura é <c>FailAsync(Message? message = null, …)</c>). Logo
    /// <c>Status.Message</c> é nulo nas duas, e nenhum classificador aplicado à
    /// task do Target consegue separá-las: a diferença não está gravada em lugar
    /// nenhum. O que as separa é o log que o próprio Target emitiu — "Falha ao
    /// adquirir o lock de contexto… para a task {TaskId}" contra "Falha ao
    /// executar o agente {AgentId} para a task {TaskId}" — e a chave para
    /// chegar até ele é o <c>TargetTaskId</c> daqui. Quem investigar uma
    /// delegação que falhou parte deste registro e vai ao log do Target; não há
    /// atalho, e inventar um classificador aqui produziria uma causa que o
    /// sistema não mediu.
    /// </para>
    /// </remarks>
    private async Task<string> WaitForTerminalStateAsync(
        PostgresTaskStore targetTaskStore,
        string targetTaskId,
        Guid targetAgentId,
        Guid sourceAgentId,
        string sourceTaskId,
        long started,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.Value.Timeout);

        // Hoistadas para fora do try: é o catch que registra, e lá dentro do
        // laço elas não existiriam (ver o remarks acima).
        TaskState? lastObservedState = null;
        DateTimeOffset? lastObservationAt = null;
        var successfulReadCount = 0;

        try
        {
            while (true)
            {
                var current = await targetTaskStore.GetTaskAsync(targetTaskId, timeoutCts.Token);

                successfulReadCount++;
                lastObservationAt = DateTimeOffset.UtcNow;
                lastObservedState = current?.Status.State;

                if (current is not null && IsTerminal(current.Status.State))
                {
                    if (current.Status.State == TaskState.Completed)
                    {
                        RecordWithTargetTask(
                            sourceTaskId, sourceAgentId, targetAgentId, targetTaskId, ExecutionMetricsValues.DelegationOutcome.Completed,
                            lastObservedState, lastObservationAt, successfulReadCount, started);

                        var text = current.Artifacts?.LastOrDefault()?.Parts.FirstOrDefault(part => part.Text is not null)?.Text;
                        return text ?? string.Empty;
                    }

                    RecordWithTargetTask(
                        sourceTaskId, sourceAgentId, targetAgentId, targetTaskId, ExecutionMetricsValues.DelegationOutcome.TargetUnsuccessful,
                        lastObservedState, lastObservationAt, successfulReadCount, started);

                    logger.LogWarning(
                        "Delegação sem sucesso: task {SourceTaskId} do agente {SourceAgentId} delegou para o agente {TargetAgentId} " +
                        "na task {TargetTaskId}, que terminou em {LastObservedTargetState} " +
                        "(observado em {LastObservationAt}, leituras bem-sucedidas: {SuccessfulReadCount}).",
                        sourceTaskId,
                        sourceAgentId,
                        targetAgentId,
                        targetTaskId,
                        current.Status.State,
                        lastObservationAt,
                        successfulReadCount);
                    return $"Delegação não concluída com sucesso (estado final: {current.Status.State}).";
                }

                await Task.Delay(options.Value.PollInterval, timeoutCts.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Só o timeout desta espera expirou (não o cancellationToken da
            // execução como um todo, que propagaria normalmente) —
            // degradação graciosa (Decision 8): resultado de falha para o
            // LLM do Source continuar, task do Source não falha por isso.
            LogTimeout(sourceTaskId, sourceAgentId, targetAgentId, targetTaskId, lastObservedState, lastObservationAt, successfulReadCount);
            RecordWithTargetTask(
                sourceTaskId, sourceAgentId, targetAgentId, targetTaskId, ExecutionMetricsValues.DelegationOutcome.Expired,
                lastObservedState, lastObservationAt, successfulReadCount, started);
            return "Delegação não concluiu dentro do tempo limite.";
        }
    }

    /// <summary>
    /// Duas emissões, e não uma com valor sentinela: a spec desta change proíbe
    /// apresentar a ausência de observação <b>como</b> um estado, e um sentinela
    /// dentro do campo de estado é exatamente isso. Quem consome lê a presença
    /// da chave <c>LastObservedTargetState</c> como "houve leitura com linha".
    /// </summary>
    private void LogTimeout(
        string sourceTaskId,
        Guid sourceAgentId,
        Guid targetAgentId,
        string targetTaskId,
        TaskState? lastObservedState,
        DateTimeOffset? lastObservationAt,
        int successfulReadCount)
    {
        if (lastObservedState is not null)
        {
            logger.LogWarning(
                "Delegação expirada: task {SourceTaskId} do agente {SourceAgentId} delegou para o agente {TargetAgentId} " +
                "na task {TargetTaskId}, que não concluiu dentro do timeout de {Timeout}. " +
                "Último estado observado: {LastObservedTargetState} (em {LastObservationAt}, leituras bem-sucedidas: {SuccessfulReadCount}).",
                sourceTaskId,
                sourceAgentId,
                targetAgentId,
                targetTaskId,
                options.Value.Timeout,
                lastObservedState,
                lastObservationAt,
                successfulReadCount);
            return;
        }

        logger.LogWarning(
            "Delegação expirada: task {SourceTaskId} do agente {SourceAgentId} delegou para o agente {TargetAgentId} " +
            "na task {TargetTaskId}, que não concluiu dentro do timeout de {Timeout}. " +
            "Nenhum estado do Target foi observado (leituras bem-sucedidas: {SuccessfulReadCount}).",
            sourceTaskId,
            sourceAgentId,
            targetAgentId,
            targetTaskId,
            options.Value.Timeout,
            successfulReadCount);
    }

    private static bool IsTerminal(TaskState state) =>
        state is TaskState.Completed or TaskState.Failed or TaskState.Rejected or TaskState.Canceled;
}
