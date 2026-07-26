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
    IChatClient chatClient,
    ILogger<AgentExecutionService> logger)
{
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
        var task = await taskStore.GetTaskAsync(message.TaskId, cancellationToken);
        if (task is null)
        {
            logger.LogError("Task {TaskId} não encontrada no store", message.TaskId);
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

        try
        {
            var userText = ExtractLatestUserText(task);
            var aiAgent = new ChatClientAgent(chatClient, agent.Instructions, agent.Name);
            var response = await aiAgent.RunAsync(userText, session: null, options: null, cancellationToken);

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
                cancellationToken);
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
        CancellationToken cancellationToken)
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
            await taskStore.SaveTaskAsync(taskId, current, cancellationToken);
        }

        return current;
    }
}
