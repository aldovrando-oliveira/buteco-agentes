using System.ComponentModel;
using System.Text.Json;
using A2A;
using Buteco.Workers.A2A;
using Buteco.Workers.Agents.Entities;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Mcp;
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
        string contextId,
        int currentDepth,
        CancellationToken cancellationToken)
    {
        var delegations = await (
            from delegation in dbContext.AgentDelegations.AsNoTracking()
            join target in dbContext.Agents.AsNoTracking() on delegation.TargetAgentId equals target.Id
            where delegation.SourceAgentId == sourceAgent.Id
            orderby delegation.TargetAgentId
            select new { delegation.TargetAgentId, TargetName = target.Name }
        ).ToListAsync(cancellationToken);

        var tools = new List<AITool>(delegations.Count);
        var usedToolNames = new HashSet<string>();

        foreach (var delegation in delegations)
        {
            var baseName = ToolNameSanitizer.Sanitize($"{ToolNamePrefix}{DelegationToolNameSlugifier.Slugify(delegation.TargetName)}");
            var toolName = baseName;
            var suffix = 2;
            while (!usedToolNames.Add(toolName))
            {
                toolName = $"{baseName}-{suffix}";
                suffix++;
            }

            tools.Add(BuildDelegationTool(toolName, delegation.TargetAgentId, delegation.TargetName, sourceAgent.Id, contextId, currentDepth));
        }

        return tools;
    }

    private AITool BuildDelegationTool(
        string toolName, Guid targetAgentId, string targetAgentName, Guid sourceAgentId, string contextId, int currentDepth)
    {
        async Task<string> DelegateAsync(
            [Description("A tarefa ou pergunta a delegar para o agente Target.")] string message,
            CancellationToken cancellationToken) =>
            await DelegateToTargetAsync(targetAgentId, sourceAgentId, contextId, currentDepth, message, cancellationToken);

        return AIFunctionFactory.Create(
            (Func<string, CancellationToken, Task<string>>)DelegateAsync,
            name: toolName,
            description: $"Delega esta tarefa para o agente '{targetAgentName}'. Aguarda a conclusão e retorna o resultado produzido por ele.");
    }

    private async Task<string> DelegateToTargetAsync(
        Guid targetAgentId,
        Guid sourceAgentId,
        string contextId,
        int currentDepth,
        string message,
        CancellationToken cancellationToken)
    {
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
            return "Delegação não realizada: o agente Target não está disponível no momento.";
        }

        var targetTaskId = Guid.NewGuid().ToString("N");
        var targetTaskStore = new PostgresTaskStore(scopeFactory, targetAgentId);

        var targetTask = await CreateDelegatedTaskAsync(targetTaskStore, targetTaskId, contextId, currentDepth + 1, message, cancellationToken);
        if (targetTask is null)
        {
            logger.LogError("Falha ao criar a task delegada {TargetTaskId} para o agente {TargetAgentId}.", targetTaskId, targetAgentId);
            return "Delegação não realizada: falha interna ao criar a task do agente Target.";
        }

        await taskJobPublisher.PublishAsync(new TaskJobMessage(targetTaskId, targetAgentId, contextId), cancellationToken);

        return await WaitForTerminalStateAsync(targetTaskStore, targetTaskId, cancellationToken);
    }

    /// <summary>
    /// Cria a task `submitted` do Target com a mensagem delegada em
    /// `History` e o contador de profundidade já em `Metadata` — mesma
    /// mecânica de fila (`AgentEventQueue`/`TaskUpdater`/`TaskProjection`)
    /// já usada por `AgentExecutionService.ApplyStepAsync`, mas gravando
    /// `Metadata` antes do primeiro `SaveTaskAsync` (Decision 6), não só na
    /// conclusão.
    /// </summary>
    private static async Task<AgentTask?> CreateDelegatedTaskAsync(
        PostgresTaskStore targetTaskStore, string targetTaskId, string contextId, int childDepth, string message, CancellationToken cancellationToken)
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

        await targetTaskStore.SaveTaskAsync(targetTaskId, task, cancellationToken);
        return task;
    }

    private async Task<string> WaitForTerminalStateAsync(PostgresTaskStore targetTaskStore, string targetTaskId, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.Value.Timeout);

        try
        {
            while (true)
            {
                var current = await targetTaskStore.GetTaskAsync(targetTaskId, timeoutCts.Token);

                if (current is not null && IsTerminal(current.Status.State))
                {
                    if (current.Status.State == TaskState.Completed)
                    {
                        var text = current.Artifacts?.LastOrDefault()?.Parts.FirstOrDefault(part => part.Text is not null)?.Text;
                        return text ?? string.Empty;
                    }

                    logger.LogWarning("Task delegada {TargetTaskId} terminou em {State}, sem sucesso.", targetTaskId, current.Status.State);
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
            logger.LogWarning("Task delegada {TargetTaskId} não concluiu dentro do timeout de {Timeout}.", targetTaskId, options.Value.Timeout);
            return "Delegação não concluiu dentro do tempo limite.";
        }
    }

    private static bool IsTerminal(TaskState state) =>
        state is TaskState.Completed or TaskState.Failed or TaskState.Rejected or TaskState.Canceled;
}
