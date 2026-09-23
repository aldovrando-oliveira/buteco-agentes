using Buteco.Workers.ExecutionMetrics.Entities;
using Buteco.Workers.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Buteco.Workers.ExecutionMetrics;

/// <summary>
/// As duas escritas das métricas de uma execução: abrir a linha pai cedo e
/// fechá-la — com filhas e resultados de delegação — depois do estado terminal
/// (design.md da change <c>metricas-execucao-coleta</c>, D3).
/// </summary>
/// <remarks>
/// <para>
/// <b>NENHUM MÉTODO DESTE TIPO LANÇA</b> (convenção 4). Falha de gravação vira
/// <c>LogWarning</c> com o <c>TaskId</c> no estado estruturado, e a execução segue.
/// Quem chama <see cref="CloseAsync"/> o faz DEPOIS de o estado terminal estar
/// gravado — então não há ordem de execução em que uma falha aqui altere o que a
/// task terminou sendo. Métrica que muda o resultado do que ela mede não é
/// métrica.
/// </para>
///
/// <para>
/// <b>Abrir cedo é o que torna visível a execução que não terminou.</b> Um crash
/// entre as duas escritas deixa a linha pai com <c>EndedAt</c> nulo e nenhuma
/// filha — as filhas vivem em memória no <see cref="ExecutionMetricsScope"/> até
/// o fechamento. Perder as filhas nesse caso é o custo aceito de não fazer uma
/// ida ao banco por requisição ao provedor; a linha aberta é o dado que a métrica
/// de "sem estado terminal" lê, e ela não existiria se nascesse só no fim.
/// </para>
///
/// <para>
/// <b>Se a abertura falhou, o fechamento insere.</b> Uma falha transitória no
/// INSERT não pode custar a execução inteira quando o banco já voltou no fim.
/// </para>
///
/// <para>
/// Sem registro em DI (D2): construído por <c>AgentExecutionService</c> com o
/// <see cref="IServiceScopeFactory"/> e o logger que ele já recebe.
/// </para>
/// </remarks>
public sealed class ExecutionMetricsWriter(IServiceScopeFactory scopeFactory, ILogger logger)
{
    // O fechamento roda num `finally`, inclusive quando a execução foi cancelada
    // por shutdown — com o token dela ele seria abortado pelo mesmo motivo que
    // encerrou a execução. Prazo próprio e curto, e só.
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(10);

    public async Task OpenAsync(TaskExecution execution, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            dbContext.TaskExecutions.Add(execution);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            LogWriteFailure(ex, "abertura", execution.TaskId);
        }
    }

    public async Task CloseAsync(TaskExecution execution, ExecutionMetricsScope metrics)
    {
        try
        {
            using var timeout = new CancellationTokenSource(CloseTimeout);
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var persisted = await dbContext.TaskExecutions
                .FirstOrDefaultAsync(row => row.TaskId == execution.TaskId, timeout.Token);

            if (persisted is null)
            {
                dbContext.TaskExecutions.Add(execution);
            }
            else
            {
                persisted.Close(execution.LockAcquiredAt, execution.EndedAt, execution.TerminalState, execution.FailurePhase);
            }

            dbContext.ProviderCalls.AddRange(metrics.ProviderCalls);
            dbContext.DelegationOutcomes.AddRange(metrics.DelegationOutcomes);

            // As linhas de embedding da BUSCA entram no mesmo SaveChangesAsync
            // (change metricas-embedding-coleta, D7): é isso que faz a chave
            // estrangeira de embedding_calls para task_executions valer por
            // construção — a filha é gravada junto com o fechamento do pai.
            dbContext.EmbeddingCalls.AddRange(metrics.EmbeddingCalls);

            await dbContext.SaveChangesAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            LogWriteFailure(ex, "fechamento", execution.TaskId);
        }
    }

    private void LogWriteFailure(Exception exception, string stage, string taskId) =>
        logger.LogWarning(
            exception,
            "Falha ao gravar métrica de execução ({MetricsWriteStage}) da task {TaskId} — o estado da task não é afetado.",
            stage,
            taskId);
}
