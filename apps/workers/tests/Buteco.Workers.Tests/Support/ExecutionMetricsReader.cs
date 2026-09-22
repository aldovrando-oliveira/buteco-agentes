using Buteco.Workers.ExecutionMetrics.Entities;
using Buteco.Workers.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Workers.Tests.Support;

/// <summary>
/// Leitura das três tabelas de métrica de execução, para os guardas da change
/// <c>metricas-execucao-coleta</c> — cinco classes de teste leem as mesmas
/// tabelas, por isso o helper é compartilhado.
///
/// <para>
/// <b>Esperar pelo FECHAMENTO, não pelo estado terminal da task.</b> A linha pai
/// é fechada DEPOIS de o estado terminal estar gravado (design.md, D3) — um teste
/// que lê as métricas assim que a task fica <c>Completed</c> corre contra o
/// fechamento e reprova de forma intermitente. <see cref="WaitForClosedExecutionAsync"/>
/// ancora no estado persistido que importa: <c>EndedAt</c> preenchido.
/// </para>
/// </summary>
public static class ExecutionMetricsReader
{
    public static async Task<TaskExecution> WaitForClosedExecutionAsync(string connectionString, string taskId, int maxAttempts = 75)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var execution = await FindExecutionAsync(connectionString, taskId);
            if (execution?.EndedAt is not null)
            {
                return execution;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"A linha de execução da task '{taskId}' não foi fechada a tempo.");
    }

    public static async Task<TaskExecution> WaitForExecutionAsync(string connectionString, string taskId, int maxAttempts = 75)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var execution = await FindExecutionAsync(connectionString, taskId);
            if (execution is not null)
            {
                return execution;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Nenhuma linha de execução da task '{taskId}' foi aberta a tempo.");
    }

    public static async Task<TaskExecution?> FindExecutionAsync(string connectionString, string taskId)
    {
        await using var dbContext = CreateDbContext(connectionString);
        return await dbContext.TaskExecutions.AsNoTracking().FirstOrDefaultAsync(row => row.TaskId == taskId);
    }

    public static async Task<IReadOnlyList<ProviderCall>> ProviderCallsAsync(string connectionString, string taskId)
    {
        await using var dbContext = CreateDbContext(connectionString);
        return await dbContext.ProviderCalls.AsNoTracking().Where(row => row.TaskId == taskId).ToListAsync();
    }

    public static async Task<IReadOnlyList<DelegationOutcome>> DelegationOutcomesAsync(string connectionString, string sourceTaskId)
    {
        await using var dbContext = CreateDbContext(connectionString);
        return await dbContext.DelegationOutcomes.AsNoTracking().Where(row => row.SourceTaskId == sourceTaskId).ToListAsync();
    }

    public static async Task<IReadOnlyList<TaskExecution>> ExecutionsForAgentAsync(string connectionString, Guid agentId)
    {
        await using var dbContext = CreateDbContext(connectionString);
        return await dbContext.TaskExecutions.AsNoTracking().Where(row => row.AgentId == agentId).ToListAsync();
    }

    private static AppDbContext CreateDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(connectionString).Options);
}
