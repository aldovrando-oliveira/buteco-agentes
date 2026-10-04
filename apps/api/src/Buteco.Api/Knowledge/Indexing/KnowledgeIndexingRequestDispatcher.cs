using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Knowledge.Indexing;

/// <summary>
/// O único caminho de publicação na fila de indexação a partir do <c>apps/api</c>
/// (design.md da change indexacao-sem-job-orfao, D3 e D7): toma pedidos de
/// <c>knowledge_indexing_requests</c>, publica cada um com confirmação do broker, e
/// apaga os publicados, tudo numa transação. Chamado no fim de cada escrita, para os
/// pedidos que ela acabou de gravar, e pela <see cref="KnowledgeIndexingRequestSweepService"/>.
///
/// <para>
/// <b>Várias instâncias.</b> A consulta é <c>FOR UPDATE SKIP LOCKED</c>: cada pedido é
/// tomado por uma transação só, e quem perde pula o pedido. Depois do commit de quem
/// venceu, o pedido não existe mais.
/// </para>
///
/// <para>
/// <b>Pelo menos uma vez.</b> O pedido é apagado depois da confirmação, na mesma
/// transação que o travou. Se o processo morrer entre as duas, o pedido volta a ser
/// visível e é publicado de novo, na mesma revisão — e o <c>apps/workers</c> a indexa
/// com o mesmo resultado.
/// </para>
///
/// <para>
/// <b>Singleton com escopo próprio por despacho</b>: o despacho no fim da escrita não
/// usa o <c>AppDbContext</c> da requisição, e a sua transação é independente da que
/// gravou o documento — que já fez commit quando ele começa.
/// </para>
/// </summary>
public sealed class KnowledgeIndexingRequestDispatcher(
    IServiceScopeFactory scopeFactory,
    IKnowledgeIndexingJobPublisher publisher,
    KnowledgeIndexingRequestSchedule schedule,
    KnowledgeIndexingDispatchWindow window,
    ILogger<KnowledgeIndexingRequestDispatcher> logger)
{
    /// <summary>Despacho no fim da escrita pulado pela janela de D8.</summary>
    public static readonly EventId RequestDispatchSkippedEvent = new(1031, "KnowledgeIndexingRequestDispatchSkipped");

    /// <summary>Despacho no fim da escrita que falhou e abriu a janela.</summary>
    public static readonly EventId RequestDispatchFailedEvent = new(1032, "KnowledgeIndexingRequestDispatchFailed");

    /// <summary>Publicação que falhou num ciclo da varredura.</summary>
    public static readonly EventId SweepPublishFailedEvent = new(1033, "KnowledgeIndexingRequestSweepPublishFailed");

    /// <summary>
    /// Despacha os pedidos que a escrita acabou de gravar. <b>Nunca lança</b>: a escrita
    /// já fez commit do documento e do pedido, e responde o sucesso de sempre qualquer
    /// que seja o resultado daqui (D2). Falha ou limite estourado deixam o pedido para a
    /// varredura e abrem a janela; com a janela aberta, nem tenta.
    /// </summary>
    public async Task DispatchAfterWriteAsync(IReadOnlyCollection<Guid> requestIds, CancellationToken requestAborted)
    {
        if (requestIds.Count == 0)
        {
            return;
        }

        if (window.IsOpen)
        {
            window.CountSkip();
            logger.LogDebug(
                RequestDispatchSkippedEvent,
                "Despacho de {Count} pedido(s) de indexação deixado para a varredura: falha recente de publicação.",
                requestIds.Count);
            return;
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        limit.CancelAfter(schedule.RequestDispatchTimeout);

        var dispatch = DispatchBatchAsync(requestIds, limit.Token);
        try
        {
            // WaitAsync, e não só o cancelamento: uma chamada que não honre o token
            // (conexão presa no timeout da própria biblioteca) não segura a resposta.
            var outcome = await dispatch.WaitAsync(schedule.RequestDispatchTimeout, requestAborted);
            if (outcome.PublishFailure is null)
            {
                window.Close();
                return;
            }

            OpenAfterRequestFailure(outcome.PublishFailure, requestIds.Count);
        }
        catch (OperationCanceledException) when (requestAborted.IsCancellationRequested)
        {
            // O cliente desistiu depois do commit. Não diz nada sobre o broker; a
            // varredura publica o pedido.
            await AbandonAsync(limit, dispatch);
        }
        catch (Exception exception)
        {
            // Limite estourado (TimeoutException), banco, ou o que mais escapar: o
            // pedido continua gravado, e a varredura o publica.
            await AbandonAsync(limit, dispatch);
            OpenAfterRequestFailure(exception, requestIds.Count);
        }
    }

    /// <summary>
    /// Um ciclo da varredura: lotes até a tabela esvaziar ou uma publicação falhar.
    /// Lança se a consulta ao banco falhar — quem chama (o serviço) captura por
    /// ciclo, e essa falha não abre a janela, porque não diz nada sobre o broker.
    /// </summary>
    /// <returns>Quantos pedidos foram publicados no ciclo.</returns>
    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var outcome = await DispatchBatchAsync(requestIds: null, cancellationToken);
            total += outcome.Published;

            if (outcome.Published > 0)
            {
                window.Close();
            }

            if (outcome.PublishFailure is not null)
            {
                window.Open();
                logger.LogWarning(
                    SweepPublishFailedEvent,
                    outcome.PublishFailure,
                    "Varredura de pedidos de indexação: publicação falhou depois de {Published} pedido(s) publicados neste ciclo; o restante fica para o próximo.",
                    total);
                return total;
            }

            if (outcome.Taken < schedule.BatchSize)
            {
                return total;
            }
        }
    }

    /// <summary>
    /// Uma transação: toma até um lote, publica em ordem até a primeira falha, apaga os
    /// publicados, commit. A falha de publicação volta no resultado, não como exceção,
    /// porque os publicados antes dela precisam ser apagados assim mesmo.
    /// </summary>
    private async Task<BatchOutcome> DispatchBatchAsync(IReadOnlyCollection<Guid>? requestIds, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var batchSize = schedule.BatchSize;
        var requests = requestIds is null
            ? await dbContext.KnowledgeIndexingRequests
                .FromSql($"""
                    SELECT * FROM knowledge_indexing_requests
                    ORDER BY "CreatedAt", "Id"
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .AsNoTracking()
                .ToListAsync(cancellationToken)
            : await dbContext.KnowledgeIndexingRequests
                .FromSql($"""
                    SELECT * FROM knowledge_indexing_requests
                    WHERE "Id" = ANY({requestIds.ToArray()})
                    ORDER BY "CreatedAt", "Id"
                    FOR UPDATE SKIP LOCKED
                    """)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

        var published = new List<Guid>(requests.Count);
        Exception? failure = null;
        foreach (var request in requests)
        {
            try
            {
                await publisher.PublishAsync(request.ToMessage(), cancellationToken);
                published.Add(request.Id);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failure = exception;
                break;
            }
        }

        if (published.Count > 0)
        {
            await dbContext.KnowledgeIndexingRequests
                .Where(request => published.Contains(request.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new BatchOutcome(requests.Count, published.Count, failure);
    }

    private void OpenAfterRequestFailure(Exception exception, int count)
    {
        window.Open();
        logger.LogWarning(
            RequestDispatchFailedEvent,
            exception,
            "Despacho de {Count} pedido(s) de indexação no fim da escrita falhou; ficam para a varredura, e as escritas pulam o despacho por {Window}.",
            count,
            schedule.SkipWindow);
    }

    /// <summary>
    /// Cancela o despacho abandonado <b>antes</b> de o <c>CancellationTokenSource</c>
    /// ser descartado: o <c>WaitAsync</c> pode estourar antes do <c>CancelAfter</c>, e um
    /// despacho que nunca recebesse o cancelamento seguraria a transação e o lock do
    /// pedido, que a varredura pularia para sempre. E observa a falha dele, para não
    /// virar exceção não observada.
    /// </summary>
    private static async Task AbandonAsync(CancellationTokenSource limit, Task dispatch)
    {
        await limit.CancelAsync();
        ObserveAbandoned(dispatch);
    }

    private static void ObserveAbandoned(Task dispatch) =>
        _ = dispatch.ContinueWith(
            task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private readonly record struct BatchOutcome(int Taken, int Published, Exception? PublishFailure);
}
