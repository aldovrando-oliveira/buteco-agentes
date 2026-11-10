using System.Text.Json;
using A2A;
using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.A2A;

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
            // O dono só na inserção (agentes-por-aplicacao, D3): nulo para o inbox, que não põe dono, e
            // nenhuma transição o muda — o Update abaixo não o recebe.
            dbContext.A2ATasks.Add(new A2ATaskRecord(
                taskId, agentId, task.ContextId, task.Status.State.ToString(), task.Status.Timestamp, payload, TaskOwnerScope.Current));
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

        // Nota: esta consulta não filtra por AgentId (só ContextId/Status),
        // apesar de A2ATaskRecord.AgentId existir. Mesma lacuna corrigida na
        // cópia de apps/workers (ver design.md da change
        // apps-workers-historico-conversa, Decisão 2). O método é alcançável
        // pelo JSON-RPC (ListTasks) por qualquer chamador autorizado na rota
        // A2A: hoje só o service:inbox, que não o chama. Para a aplicação
        // externa ele é fechado, com 403, antes do SDK (ApplicationA2AFilter;
        // change agentes-por-aplicacao, D1), porque listaria tasks de todo
        // mundo. Se ganhar consumidor, o filtro por agente vem junto.
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
