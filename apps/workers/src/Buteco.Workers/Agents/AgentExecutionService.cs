using System.Text.Json;
using global::A2A;
using Buteco.Workers.A2A;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Messaging;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Buteco.Workers.Agents;

public sealed class AgentExecutionService(
    IServiceScopeFactory scopeFactory,
    IChatClientResolver chatClientResolver,
    ILogger<AgentExecutionService> logger)
{
    // Constante global, não configurável por agente (non-goal explícito —
    // ver design.md, Goals/Non-Goals e Decisão 4). Ajustar aqui depois de
    // observar custo/latência real (ver Open Questions do design.md).
    private const int MaxHistoryMessages = 20;

    private const string ConversationSessionMetadataKey = "conversationSession";

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

            // agent.Provider/agent.Model só ficam nulos para um agente "precisa de
            // reconfiguração" — apps/api já rejeita SendMessage nesse caso antes de
            // publicar o job (EnqueueingAgentHandler), então nunca deveriam chegar
            // aqui; o operador nulo-tolerante (!) documenta essa garantia, não a
            // ignora — se ela falhar, o resolver/aiAgent lançará e cairá no catch
            // abaixo, terminando a task como failed (Decision 5).
            var chatClient = chatClientResolver.Resolve(agent.Provider!, agent.Model!);
            var aiAgent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
            {
                Name = agent.Name,
                ChatOptions = new ChatOptions { Instructions = agent.Instructions },
                ChatHistoryProvider = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions
                {
                    ChatReducer = new RecentMessageChatReducer(MaxHistoryMessages),
                }),
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

            await ApplyStepAsync(
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
                completedTask => completedTask.Metadata = new Dictionary<string, JsonElement>
                {
                    [ConversationSessionMetadataKey] = conversationSessionValue,
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao executar o agente {AgentId} para a task {TaskId}", message.AgentId, message.TaskId);

            await ApplyStepAsync(
                taskStore,
                message.TaskId,
                message.ContextId,
                task,
                updater => updater.FailAsync(cancellationToken: cancellationToken),
                cancellationToken);
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
