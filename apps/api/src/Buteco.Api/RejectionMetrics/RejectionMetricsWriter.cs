using Buteco.Api.Infrastructure;
using Buteco.Api.RejectionMetrics.Entities;

namespace Buteco.Api.RejectionMetrics;

/// <summary>
/// A única escrita da métrica de recusa: uma linha por task recusada na entrada
/// (design.md, D8).
/// </summary>
/// <remarks>
/// <para>
/// <b>ESTE MÉTODO NÃO LANÇA</b> (convenção 4), e o molde é o
/// <c>ExecutionMetricsWriter</c> de <c>apps/workers</c>, cuja regra vale palavra
/// por palavra aqui: <i>métrica que muda o resultado do que ela mede não é
/// métrica</i>. Falha de gravação vira <c>LogWarning</c> com o <c>TaskId</c> no
/// estado estruturado, e a recusa segue valendo.
/// </para>
///
/// <para>
/// <b>Quem chama o faz DEPOIS de a task já estar recusada</b> — então não existe
/// ordem de execução em que uma falha aqui altere o estado em que a task terminou
/// nem faça o job ser publicado.
/// </para>
///
/// <para>
/// <b>Escopo próprio</b>, e não o <c>AppDbContext</c> de uma requisição: o
/// <c>EnqueueingAgentHandler</c> é construído por agente pelo
/// <c>AgentA2AServerRegistry</c>, fora do contêiner de requisição, e já resolve o
/// contexto assim para ler o estado do agente.
/// </para>
///
/// <para>
/// <b>O instante vem do <see cref="TimeProvider"/> registrado</b>, não de
/// <c>DateTimeOffset.UtcNow</c>: é o que torna a janela da agregação verificável
/// com <c>FakeTimeProvider</c> nos guardas das duas rotas.
/// </para>
/// </remarks>
public sealed class RejectionMetricsWriter(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<RejectionMetricsWriter> logger)
{
    public async Task WriteAsync(string taskId, Guid agentId, string reason, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            dbContext.TaskRejections.Add(
                new TaskRejection(taskId, agentId, reason, timeProvider.GetUtcNow()));

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Falha ao gravar a métrica de recusa da task {TaskId} (motivo {RejectionReason}) — o estado da task não é afetado.",
                taskId,
                reason);
        }
    }
}
