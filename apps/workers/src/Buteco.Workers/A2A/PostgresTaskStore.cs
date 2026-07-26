using System.Text.Json;
using global::A2A;
using Buteco.Workers.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Workers.A2A;

/// <summary>
/// Implementação independente da de <c>apps/api</c> (mesma tabela `a2a_tasks`,
/// dois <see cref="AppDbContext"/> distintos — ver Decisão 3/8 em design.md).
/// </summary>
public sealed class PostgresTaskStore(IServiceScopeFactory scopeFactory, Guid agentId) : ITaskStore
{
    public async Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var record = await dbContext.A2ATasks
            .AsNoTracking()
            .FirstOrDefaultAsync(task => task.TaskId == taskId, cancellationToken);

        return record is null ? null : Deserialize(record.Payload);
    }

    public async Task SaveTaskAsync(string taskId, AgentTask task, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var payload = Serialize(task);
        var record = await dbContext.A2ATasks.FirstOrDefaultAsync(t => t.TaskId == taskId, cancellationToken);

        if (record is null)
        {
            dbContext.A2ATasks.Add(new A2ATaskRecord(taskId, agentId, task.ContextId, task.Status.State.ToString(), task.Status.Timestamp, payload));
        }
        else
        {
            record.Update(task.ContextId, task.Status.State.ToString(), task.Status.Timestamp, payload);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await dbContext.A2ATasks
            .Where(task => task.TaskId == taskId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var query = dbContext.A2ATasks.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(request.ContextId))
        {
            query = query.Where(task => task.ContextId == request.ContextId);
        }

        if (request.Status.HasValue)
        {
            var status = request.Status.Value.ToString();
            query = query.Where(task => task.State == status);
        }

        var records = await query
            .OrderByDescending(task => task.StatusTimestamp)
            .Take(request.PageSize ?? 50)
            .ToListAsync(cancellationToken);

        var tasks = records.Select(record => Deserialize(record.Payload)!).ToList();

        return new ListTasksResponse
        {
            Tasks = tasks,
            NextPageToken = string.Empty,
            PageSize = tasks.Count,
            TotalSize = tasks.Count,
        };
    }

    private static AgentTask? Deserialize(string payload) =>
        JsonSerializer.Deserialize<AgentTask>(payload, A2AJsonUtilities.DefaultOptions);

    private static string Serialize(AgentTask task) =>
        JsonSerializer.Serialize(task, A2AJsonUtilities.DefaultOptions);
}
