using Buteco.Workers.EmbeddingMetrics.Entities;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Knowledge.Indexing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Buteco.Workers.EmbeddingMetrics;

/// <summary>
/// A escrita das métricas de uma tentativa de indexação: a linha de tentativa e
/// as linhas de chamada ao gateway, num <c>SaveChangesAsync</c> só, depois de o
/// estado do documento estar gravado (design.md, D7).
/// </summary>
/// <remarks>
/// <para>
/// <b>NENHUM MÉTODO DESTE TIPO LANÇA</b> (convenção 4). Falha de gravação vira
/// <c>LogWarning</c> com o <c>KnowledgeDocumentId</c> no estado estruturado, e o
/// <c>KnowledgeIndexingOutcome</c> devolvido ao consumidor é o mesmo que seria
/// sem coleta. Métrica que muda o resultado do que ela mede não é métrica.
/// </para>
///
/// <para>
/// <b>Uma escrita, e não o par "abrir cedo / fechar depois" da etapa 1.</b> Lá a
/// linha aberta <b>é</b> o dado de "task sem estado terminal", e não existiria de
/// outro jeito. Aqui esse sinal já existe e é de outra tabela:
/// <c>MarkAttemptStartedAsync</c> conta a tentativa e move o documento para
/// <c>Indexing</c> antes do trabalho, e um processo que morre no meio deixa o
/// documento em <c>Indexing</c> — visível na tela de documentos hoje, sem coleta
/// nenhuma. O custo aceito é que esse crash perde as linhas dos lotes já
/// gerados.
/// </para>
///
/// <para>
/// <b>Atômica entre pai e filhas</b>, e é isso que faz a chave estrangeira
/// <c>Restrict</c> de <c>embedding_calls</c> valer por construção.
/// </para>
///
/// <para>
/// Sem registro em DI (D4): construído por <c>KnowledgeIndexingService</c> com o
/// <see cref="IServiceScopeFactory"/> e o logger que ele <b>já</b> recebe —
/// nenhum construtor muda.
/// </para>
/// </remarks>
public sealed class EmbeddingMetricsWriter(IServiceScopeFactory scopeFactory, ILogger logger)
{
    // A escrita roda depois do estado terminal do documento, inclusive quando a
    // execução foi cancelada por shutdown — com o token dela seria abortada pelo
    // mesmo motivo que encerrou o trabalho. Prazo próprio e curto, como no
    // fechamento da etapa 1.
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(10);

    public async Task WriteAsync(
        KnowledgeIndexingAttemptContext context, KnowledgeIndexingOutcome outcome, DateTimeOffset endedAt)
    {
        // A tentativa que não foi contada não tem linha (D8): é o descarte que
        // acontece ANTES do trabalho, e ele não chegou a chamar o gateway.
        if (!context.Counted)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(WriteTimeout);
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var outcomeText = OutcomeTextOf(outcome);
            var indexed = outcome == KnowledgeIndexingOutcome.Indexed;

            dbContext.KnowledgeIndexingAttempts.Add(new KnowledgeIndexingAttempt(
                context.AttemptId,
                context.KnowledgeDocumentId,
                context.KnowledgeBaseId,
                context.ContentRevision,
                context.Attempt,
                context.MaxAttempts,
                context.StartedAt,
                endedAt,
                outcomeText,
                // Fase só quando não indexou E não foi descarte: descarte não é
                // falha — o trabalho novo já está na fila —, e gravar fase nele
                // afirmaria um defeito que não houve.
                indexed || outcome == KnowledgeIndexingOutcome.Discarded ? null : context.Phase,
                indexed ? context.FragmentCount : null));

            dbContext.EmbeddingCalls.AddRange(context.Calls);

            await dbContext.SaveChangesAsync(timeout.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Falha ao gravar métrica de embedding do documento {KnowledgeDocumentId} — o resultado da indexação não é afetado.",
                context.KnowledgeDocumentId);
        }
    }

    /// <summary>
    /// O enum é o contrato entre serviço e consumidor; o texto é o contrato com
    /// o banco. A tradução é explícita para que acrescentar um valor ao enum não
    /// grave silenciosamente um nome que a consulta não espera (convenção 12).
    /// </summary>
    private static string OutcomeTextOf(KnowledgeIndexingOutcome outcome) => outcome switch
    {
        KnowledgeIndexingOutcome.Indexed => EmbeddingMetricsValues.Outcome.Indexed,
        KnowledgeIndexingOutcome.RetryScheduled => EmbeddingMetricsValues.Outcome.RetryScheduled,
        KnowledgeIndexingOutcome.Failed => EmbeddingMetricsValues.Outcome.Failed,
        KnowledgeIndexingOutcome.Discarded => EmbeddingMetricsValues.Outcome.Discarded,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Desfecho de indexação sem texto correspondente."),
    };
}
