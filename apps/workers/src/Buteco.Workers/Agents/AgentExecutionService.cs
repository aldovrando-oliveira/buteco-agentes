using System.Globalization;
using System.Text.Json;
using global::A2A;
using Buteco.Workers.A2A;
using Buteco.Workers.AgentDelegations;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Mcp;
using Buteco.Workers.Messaging;
using Buteco.Workers.Notifications;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Buteco.Workers.Agents;

public sealed class AgentExecutionService(
    IServiceScopeFactory scopeFactory,
    IChatClientResolver chatClientResolver,
    IMcpToolSetResolver mcpToolSetResolver,
    IAgentDelegationToolSetResolver delegationToolSetResolver,
    ToolNameDeduplicator toolNameDeduplicator,
    PushNotificationSender pushNotificationSender,
    TimeProvider timeProvider,
    ILogger<AgentExecutionService> logger)
{
    // Teto de segurança, não um limiar concorrente com
    // SummarizationTurnThreshold — ver design.md da change
    // apps-workers-resumo-historico-conversa, Decisão 9. Decompilando
    // CompactionMessageIndex.Update, confirmou-se que RecentMessageChatReducer
    // truncando ativamente invalida o bookkeeping incremental do
    // CompactionProvider a partir do momento em que a conversa ultrapassa
    // esse valor — por isso ele precisa ficar bem acima da faixa de operação
    // normal do resumo (SummarizationTurnThreshold), não próximo dela.
    // Constante global, não configurável por agente (non-goal explícito —
    // ver design.md da change apps-workers-historico-conversa,
    // Goals/Non-Goals e Decisão 4). Ajustar aqui depois de observar
    // custo/latência real (ver Open Questions dos dois design.md).
    private const int MaxHistoryMessages = 200;

    // Limiar de interações (turnos de usuário) que dispara o resumo do
    // histórico — constante global, não configurável por agente (non-goal
    // explícito, ver design.md da change apps-workers-resumo-historico-conversa,
    // Goals/Non-Goals). Precisa ficar maior que MinimumPreservedGroups do
    // SummarizationCompactionStrategy (padrão do pacote: 8), senão não há
    // conteúdo elegível para resumir no primeiro gatilho — ver Open
    // Questions daquele design.md.
    private const int SummarizationTurnThreshold = 10;

    private const string ConversationSessionMetadataKey = "conversationSession";

    // Chaves de contexto de canal (design.md da change inbox-contexto-canal,
    // D3/D4) — mesmo nome usado do lado da escrita em apps/inbox
    // (DebounceSweepService), duplicado deliberadamente (apps isolados sem
    // ProjectReference cruzado). Sem classe Codec dedicada: ao contrário de
    // DelegationDepth/ConversationSessionCodec/MessageInstantCodec, não há
    // parsing a encapsular — os dois valores são strings cruas (D4).
    private const string ChannelTypeMetadataKey = "channelType";
    private const string ContactExternalIdMetadataKey = "contactExternalId";

    // Teto de saltos de delegação numa mesma cadeia desde a mensagem
    // original — constante global, não configurável por agente (non-goal
    // explícito, ver design.md da change apps-workers-delegacao-execucao,
    // Decision 6), mesmo estilo de MaxHistoryMessages/SummarizationTurnThreshold
    // acima. Cada nível a mais na cadeia ocupa mais uma instância de
    // apps/workers presa em espera (ver Decision 1 daquele design.md).
    private const int DelegationDepthLimit = 5;

    // Ver design.md, Decisão 9: absorve a corrida entre o publish no RabbitMQ
    // e o commit do SaveTaskAsync do evento "submitted" em apps/api.
    private const int GetTaskMaxAttempts = 5;
    private static readonly TimeSpan GetTaskRetryDelay = TimeSpan.FromMilliseconds(100);

    public async Task ExecuteAsync(TaskJobMessage message, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var agent = await dbContext.Agents
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == message.AgentId, cancellationToken);

        if (agent is null)
        {
            logger.LogError("Agente {AgentId} não encontrado para a task {TaskId}", message.AgentId, message.TaskId);
            return;
        }

        var taskStore = new PostgresTaskStore(scopeFactory, message.AgentId);

        // Retry curto: EnqueueingAgentHandler grava a task em DOIS eventos
        // separados — SubmitAsync() cria a linha (History nulo), e só depois
        // (após uma leitura de estado do agente) EnqueueMessageAsync grava a
        // mensagem do usuário nela — enquanto, concorrentemente, publica no
        // RabbitMQ. Nenhuma dessas escritas espera a outra nem o publish. Um
        // worker pode consumir a mensagem e achar a task já criada mas ainda
        // sem a mensagem do usuário em History. Por isso o retry exige task
        // encontrada E com uma mensagem de usuário utilizável, não só task
        // encontrada. Ver design.md da change apps-workers-historico-conversa,
        // Decisão 9 (achado via teste manual, não causado por esta change,
        // corrigido aqui por decisão explícita — non-goal original não se
        // aplica a este fix porque ele não toca apps/api).
        var task = await GetTaskWithRetryAsync(taskStore, message.TaskId, cancellationToken);
        if (task is null)
        {
            logger.LogError("Task {TaskId} não encontrada no store (ou sem mensagem do usuário) após retries", message.TaskId);
            return;
        }

        // Controle de profundidade da cadeia de delegação (design.md,
        // Decisão 6): checado antes de StartWorkAsync/do lock consultivo —
        // uma task além do teto nunca chega a rodar o LLM. Rejeitada (não
        // failed) para não passar pelo tratamento de erro genérico do catch
        // abaixo — mesmo estado terminal já usado para "agente inativo"/
        // "sem provider" em EnqueueingAgentHandler, que a tool de delegação
        // que está esperando (Decisão 8) já trata como qualquer outra
        // falha graciosa, sem código especial para profundidade.
        var delegationDepth = DelegationDepth.Read(task);
        if (delegationDepth > DelegationDepthLimit)
        {
            logger.LogWarning(
                "Task {TaskId} excede a profundidade máxima de delegação ({Limit}) — rejeitada.",
                message.TaskId,
                DelegationDepthLimit);

            await ApplyStepAsync(
                taskStore,
                message.TaskId,
                message.ContextId,
                task,
                updater => updater.RejectAsync(cancellationToken: cancellationToken),
                cancellationToken);
            return;
        }

        task = await ApplyStepAsync(
            taskStore,
            message.TaskId,
            message.ContextId,
            task,
            updater => updater.StartWorkAsync(cancellationToken: cancellationToken),
            cancellationToken);

        if (task is null)
        {
            logger.LogError("Task {TaskId} ficou nula após transição para working", message.TaskId);
            return;
        }

        // Serializa o processamento de mensagens do mesmo (agentId,
        // contextId) entre instâncias concorrentes do worker — ver
        // design.md, Decisão 7. Cobre a seção crítica inteira: leitura da
        // sessão anterior → RunAsync → escrita da nova.
        await using var contextLock = await ConversationContextLock.AcquireAsync(
            scopeFactory, message.AgentId, message.ContextId, cancellationToken);

        try
        {
            var userText = ExtractLatestUserText(task);
            var messageInstant = ExtractMessageInstant(task, message.TaskId);
            var (channelType, contactExternalId) = ExtractChannelContext(task, message.TaskId);

            // agent.Provider/agent.Model só ficam nulos para um agente "precisa de
            // reconfiguração" — apps/api já rejeita SendMessage nesse caso antes de
            // publicar o job (EnqueueingAgentHandler), então nunca deveriam chegar
            // aqui; o operador nulo-tolerante (!) documenta essa garantia, não a
            // ignora — se ela falhar, o resolver/aiAgent lançará e cairá no catch
            // abaixo, terminando a task como failed (Decision 5).
            // COMPARTILHADO E NÃO DESCARTÁVEL: o resolver devolve a MESMA
            // instância para o mesmo (provider, model) durante toda a vida do
            // processo (change fix-vazamento-httpclient-chat). Não acrescentar
            // `using`/`await using` aqui nem no aiAgent montado abaixo — o
            // contraste com o `await using` do toolSet logo adiante é
            // deliberado, e é justamente a assimetria que engana:
            // DelegatingChatClient.Dispose() descarta o InnerClient em cascata,
            // e ChatClientAgent empilha middleware delegante sobre este client,
            // então um `using` aqui quebraria TODA mensagem seguinte daquele par
            // com ObjectDisposedException. Hoje nada descarta (ChatClientAgent
            // não é IDisposable), e é essa garantia que
            // TaskJobConsumerTests.Consumer_ProcessesTwoTasksInSequence_SharedChatClientIsNeverDisposed
            // prende.
            var chatClient = chatClientResolver.Resolve(agent.Provider!, agent.Model!);

            // Precisa ficar vivo durante todo o RunAsync abaixo, não só
            // durante a resolução — cada AITool devolvido encapsula uma
            // conexão MCP viva (design.md da change apps-workers-execucao-mcp,
            // Decision 4). await using cobre tanto o caminho de sucesso
            // quanto uma exceção propagando para o catch abaixo.
            await using var toolSet = await mcpToolSetResolver.ResolveAsync(dbContext, message.AgentId, cancellationToken);

            // Sem conexão externa viva por trás (diferente de McpToolSet) —
            // não precisa de await using, ver design.md, Decision 10.
            var delegationTools = await delegationToolSetResolver.ResolveAsync(
                dbContext, agent, message.ContextId, delegationDepth, messageInstant, cancellationToken);

            // Bloco de contexto temporal concatenado às Instructions do
            // operador — sem tocar Agent.Instructions no banco, sem entrar
            // no histórico de conversa (design.md da change
            // apps-workers-contexto-temporal, Decisões 1 e 2). Instructions
            // vazia (Non-Goal: sem validação de não-vazio em apps/api) não
            // pode deixar separador órfão — usa só o bloco nesse caso.
            var temporalContextBlock = TemporalContextBlockBuilder.Build(timeProvider, messageInstant);
            var instructionsWithTemporalContext = TemporalContextBlockBuilder.Concatenate(agent.Instructions, temporalContextBlock);

            // Bloco de contexto de canal, concatenado depois do bloco
            // temporal — reaproveita TemporalContextBlockBuilder.Concatenate
            // uma segunda vez em vez de estender sua assinatura (design.md,
            // D5). Omitido inteiramente quando os dois campos estão
            // ausentes (Build retorna null) — Concatenate não é chamado
            // nesse caso, preservando as Instructions com só o bloco
            // temporal.
            var channelContextBlock = ChannelContextBlockBuilder.Build(channelType, contactExternalId);
            var instructionsWithContext = channelContextBlock is null
                ? instructionsWithTemporalContext
                : TemporalContextBlockBuilder.Concatenate(instructionsWithTemporalContext, channelContextBlock);

            var aiAgent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
            {
                Name = agent.Name,
                // Dedupe global no ponto de concatenação (design.md da change
                // dedupe-global-nome-de-tool, Decisão 1): é o único ponto que
                // sabe que os dois conjuntos dividem espaço de nome. Antes era
                // `toolSet.Tools.Concat(delegationTools)` cru, e um nome
                // duplicado era sombreado em silêncio por
                // FunctionInvokingChatClient.FindTool. A ordem dos argumentos é
                // a precedência declarada (Decisão 5): MCP mantém o nome, a
                // tool de delegação é a renomeada.
                ChatOptions = new ChatOptions
                {
                    Instructions = instructionsWithContext,
                    Tools = [.. toolNameDeduplicator.Deduplicate(message.AgentId, toolSet.Tools, delegationTools)],
                },
                ChatHistoryProvider = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions
                {
                    ChatReducer = new RecentMessageChatReducer(MaxHistoryMessages),
                }),
                // Compaction (Microsoft.Agents.AI.Compaction) é
                // [Experimental("MAAI001")] — risco aceito, ver design.md da
                // change apps-workers-resumo-historico-conversa, Decisão 1/8.
                // Reaproveita o mesmo chatClient resolvido acima para a
                // chamada de resumo — sem client dedicado (Decisão 4).
#pragma warning disable MAAI001
                AIContextProviders = new AIContextProvider[]
                {
                    new CompactionProvider(new SummarizationCompactionStrategy(
                        chatClient,
                        CompactionTriggers.TurnsExceed(SummarizationTurnThreshold))),
                },
#pragma warning restore MAAI001
            });

            var session = await LoadSessionAsync(aiAgent, taskStore, message.ContextId, cancellationToken);

            var response = await aiAgent.RunAsync(userText, session, options: null, cancellationToken);

            // Serializado antes do SaveTaskAsync final (não no catch — ver
            // design.md, Decisões 1 e 6): se DeserializeSessionAsync tivesse
            // falhado antes, ou se RunAsync lançar, cai no catch abaixo sem
            // nunca chegar aqui, e a última sessão persistida (de uma task
            // completed anterior) permanece intacta.
            var serializedSession = await aiAgent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
            var conversationSessionValue = ConversationSessionCodec.Encode(serializedSession);

            var parts = new List<Part> { Part.FromText(response.Text) };

            var savedTask = await ApplyStepAsync(
                taskStore,
                message.TaskId,
                message.ContextId,
                task,
                async updater =>
                {
                    await updater.AddArtifactAsync(parts, cancellationToken: cancellationToken);
                    await updater.CompleteAsync(cancellationToken: cancellationToken);
                },
                cancellationToken,
                completedTask => completedTask.Metadata = BuildTerminalMetadata(conversationSessionValue, message.PushNotificationConfig));

            await SendPushNotificationIfConfiguredAsync(message, savedTask, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao executar o agente {AgentId} para a task {TaskId}", message.AgentId, message.TaskId);

            var savedTask = await ApplyStepAsync(
                taskStore,
                message.TaskId,
                message.ContextId,
                task,
                updater => updater.FailAsync(cancellationToken: cancellationToken),
                cancellationToken,
                message.PushNotificationConfig is not null
                    ? failedTask => failedTask.Metadata = BuildTerminalMetadata(conversationSessionValue: null, message.PushNotificationConfig)
                    : null);

            await SendPushNotificationIfConfiguredAsync(message, savedTask, cancellationToken);
        }
    }

    /// <summary>
    /// Dispara o webhook (design.md, Decision 2) só quando a mensagem trouxe
    /// um <c>PushNotificationConfig</c> — a task já está persistida com seu
    /// estado terminal real (<paramref name="task"/> é o retorno de
    /// <see cref="ApplyStepAsync"/>, chamado depois do <c>SaveTaskAsync</c>)
    /// antes desta chamada começar.
    /// </summary>
    private async Task SendPushNotificationIfConfiguredAsync(TaskJobMessage message, AgentTask? task, CancellationToken cancellationToken)
    {
        if (message.PushNotificationConfig is not null && task is not null)
        {
            await pushNotificationSender.SendAsync(message.PushNotificationConfig, task, cancellationToken);
        }
    }

    /// <summary>
    /// Recupera o <see cref="AgentSession"/> da task completed mais recente
    /// do mesmo contextId (a sessão serializada já é cumulativa — não é
    /// necessário paginar por várias tasks, ver design.md, Decisão 3), ou
    /// cria uma sessão nova se não houver nenhuma (contextId novo, ou task
    /// gravada antes desta mudança, sem o campo).
    /// </summary>
    private static async Task<AgentSession> LoadSessionAsync(
        AIAgent aiAgent,
        ITaskStore taskStore,
        string contextId,
        CancellationToken cancellationToken)
    {
        var previousCompleted = await taskStore.ListTasksAsync(new ListTasksRequest
        {
            ContextId = contextId,
            Status = TaskState.Completed,
            PageSize = 1,
        }, cancellationToken);

        var metadata = previousCompleted.Tasks.FirstOrDefault()?.Metadata;
        if (metadata is not null && metadata.TryGetValue(ConversationSessionMetadataKey, out var conversationSessionValue))
        {
            var decoded = ConversationSessionCodec.Decode(conversationSessionValue);
            return await aiAgent.DeserializeSessionAsync(decoded, cancellationToken: cancellationToken);
        }

        return await aiAgent.CreateSessionAsync(cancellationToken);
    }

    /// <summary>
    /// Tenta ler a task algumas vezes, com um pequeno atraso entre tentativas,
    /// até encontrar uma task que já tenha uma mensagem de usuário utilizável
    /// — não basta a task existir, ver comentário em <see cref="ExecuteAsync"/>
    /// e design.md, Decisão 9.
    /// </summary>
    private static async Task<AgentTask?> GetTaskWithRetryAsync(
        ITaskStore taskStore, string taskId, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= GetTaskMaxAttempts; attempt++)
        {
            var task = await taskStore.GetTaskAsync(taskId, cancellationToken);
            if (task is not null && !string.IsNullOrWhiteSpace(ExtractLatestUserText(task)))
            {
                return task;
            }

            if (attempt < GetTaskMaxAttempts)
            {
                await Task.Delay(GetTaskRetryDelay, cancellationToken);
            }
        }

        return null;
    }

    private static string ExtractLatestUserText(AgentTask task)
    {
        var lastUserMessage = task.History?.LastOrDefault(m => m.Role == Role.User);
        return lastUserMessage?.Parts.FirstOrDefault(part => part.Text is not null)?.Text ?? string.Empty;
    }

    /// <summary>
    /// Lê <c>Message.Metadata[MessageInstantCodec.MetadataKey]</c> da última
    /// mensagem do usuário no histórico da task — mesmo filtro de
    /// <see cref="ExtractLatestUserText"/> (design.md da change
    /// inbox-instante-mensagem, Decisão D3: invariante verificado contra o
    /// código real, não assumido — o Message que uma task delegada recebe
    /// satisfaz este mesmo filtro). Três casos tratados como "sem instante
    /// de mensagem", nunca como erro de task (Decisão D4): Metadata nulo,
    /// chave ausente, ou valor presente mas não parseável como ISO 8601 —
    /// esse terceiro caso, e só ele, emite um log de aviso, porque um valor
    /// presente e ilegível é sinal de bug em algum produtor da chave.
    /// </summary>
    private DateTimeOffset? ExtractMessageInstant(AgentTask task, string taskId)
    {
        var lastUserMessage = task.History?.LastOrDefault(m => m.Role == Role.User);
        if (lastUserMessage?.Metadata is null || !lastUserMessage.Metadata.TryGetValue(MessageInstantCodec.MetadataKey, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
        {
            return instant;
        }

        logger.LogWarning(
            "Task {TaskId} tem {MetadataKey} presente em Message.Metadata, mas com um valor ilegível como instante ISO 8601: {RawValue}",
            taskId,
            MessageInstantCodec.MetadataKey,
            value.GetRawText());
        return null;
    }

    /// <summary>
    /// Lê <c>channelType</c>/<c>contactExternalId</c> de
    /// <c>Message.Metadata</c> da última mensagem do usuário no histórico da
    /// task — mesmo ponto de leitura de <see cref="ExtractMessageInstant"/>,
    /// sem caminho de extração separado para tasks delegadas (design.md,
    /// D6: contexto de canal não se propaga na delegação).
    /// </summary>
    private (string? ChannelType, string? ContactExternalId) ExtractChannelContext(AgentTask task, string taskId)
    {
        var lastUserMessage = task.History?.LastOrDefault(m => m.Role == Role.User);
        var metadata = lastUserMessage?.Metadata;

        return (
            ExtractChannelContextField(metadata, ChannelTypeMetadataKey, taskId),
            ExtractChannelContextField(metadata, ContactExternalIdMetadataKey, taskId));
    }

    /// <summary>
    /// Por campo (design.md, D6): metadata nulo, chave ausente, ou string
    /// vazia — ausência silenciosa, sem log (caminho normal, ex. cliente A2A
    /// externo que não conhece a chave). Valor presente com
    /// <see cref="JsonValueKind"/> diferente de <see cref="JsonValueKind.String"/>
    /// — tratado como ausência para o bloco, mas registra log de aviso: é
    /// sinal de bug em algum produtor da chave (nunca <c>apps/inbox</c>, que
    /// só grava strings — mas um cliente A2A externo pode), mesmo padrão de
    /// <see cref="ExtractMessageInstant"/> para valor ilegível.
    /// </summary>
    private string? ExtractChannelContextField(IReadOnlyDictionary<string, JsonElement>? metadata, string key, string taskId)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var stringValue = value.GetString();
            return string.IsNullOrEmpty(stringValue) ? null : stringValue;
        }

        logger.LogWarning(
            "Task {TaskId} tem {MetadataKey} presente em Message.Metadata, mas com um valor que não é string: {RawValue}",
            taskId,
            key,
            value.GetRawText());
        return null;
    }

    /// <summary>
    /// Monta o Metadata gravado numa transição terminal (completed/failed) —
    /// <c>conversationSession</c> só no caminho de sucesso (sessão só existe
    /// quando o LLM respondeu), <c>pushNotificationConfig</c> em qualquer um
    /// dos dois quando presente na mensagem (design.md, Decision 1).
    /// </summary>
    private static Dictionary<string, JsonElement> BuildTerminalMetadata(
        JsonElement? conversationSessionValue, PushNotificationConfig? pushNotificationConfig)
    {
        var metadata = new Dictionary<string, JsonElement>();

        if (conversationSessionValue is not null)
        {
            metadata[ConversationSessionMetadataKey] = conversationSessionValue.Value;
        }

        if (pushNotificationConfig is not null)
        {
            metadata[PushNotificationConfigCodec.MetadataKey] = PushNotificationConfigCodec.Encode(pushNotificationConfig);
        }

        return metadata;
    }

    private static async Task<AgentTask?> ApplyStepAsync(
        ITaskStore taskStore,
        string taskId,
        string contextId,
        AgentTask? current,
        Func<TaskUpdater, ValueTask> step,
        CancellationToken cancellationToken,
        Action<AgentTask>? beforeSave = null)
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, taskId, contextId);

        await step(updater);
        queue.Complete();

        await foreach (var streamEvent in queue.WithCancellation(cancellationToken))
        {
            current = TaskProjection.Apply(current, streamEvent);
        }

        if (current is not null)
        {
            beforeSave?.Invoke(current);
            await taskStore.SaveTaskAsync(taskId, current, cancellationToken);
        }

        return current;
    }
}
