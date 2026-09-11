using Buteco.Workers.Infrastructure;
using Buteco.Workers.Knowledge.Chunking;
using Buteco.Workers.Knowledge.Embedding;
using Buteco.Workers.Knowledge.Entities;
using Buteco.Workers.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pgvector;

namespace Buteco.Workers.Knowledge.Indexing;

/// <summary>
/// A unidade de trabalho da indexação: lê o documento, fragmenta, gera embedding
/// em lote e grava — ou decide o que fazer quando algo falha.
/// </summary>
public sealed class KnowledgeIndexingService(
    IServiceScopeFactory scopeFactory,
    IKnowledgeChunker chunker,
    IEmbeddingGeneratorResolver embeddingResolver,
    IOptions<EmbeddingOptions> embeddingOptions,
    TimeProvider timeProvider,
    ILogger<KnowledgeIndexingService> logger)
{
    public async Task<KnowledgeIndexingOutcome> IndexAsync(KnowledgeIndexingJobMessage message, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var document = await dbContext.KnowledgeDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == message.KnowledgeDocumentId, cancellationToken);

        // Documento sumiu antes de começarmos, ou a revisão já não é a que pediu
        // o trabalho: descartar SEM contar tentativa. Não é falha — o trabalho
        // novo já está na fila (design.md, D5).
        if (document is null || document.ContentRevision != message.ContentRevision)
        {
            logger.LogInformation(
                "Indexação descartada antes de começar para o documento {DocumentId}: revisão pedida {Requested}, corrente {Current}",
                message.KnowledgeDocumentId, message.ContentRevision, document?.ContentRevision);
            return KnowledgeIndexingOutcome.Discarded;
        }

        // A tentativa é contada AQUI, antes do trabalho, e vale para as duas
        // saídas do catch. Contar só no fim faria a execução que estoura por
        // timeout não aparecer no contador — e o operador veria "2 tentativas"
        // onde houve 3.
        var now = timeProvider.GetUtcNow();
        await MarkAttemptStartedAsync(dbContext, message, now, cancellationToken);

        try
        {
            var fragments = chunker.Chunk(document.ExtractedText);

            // Guarda de "sucesso com zero fragmentos é recusado" (spec própria).
            // NÃO é contra documento vazio, que é inalcançável pela API — o
            // extrator recusa conteúdo em branco na criação e na atualização. É
            // defesa em profundidade contra REGRESSÃO NO FRAGMENTADOR, que é o
            // caminho alcançável para a contagem zerada com aparência de
            // sucesso, o pior caso da convenção 13.
            //
            // Sem esta guarda, um fragmentador defeituoso deixaria o documento
            // em Indexed com zero fragmentos e nada reprovaria.
            if (fragments.Count == 0)
            {
                return await FailAsync(dbContext, message, KnowledgeIndexingFailure.EmptyFragmentSet, cancellationToken);
            }

            var options = embeddingOptions.Value;
            var generator = embeddingResolver.Resolve();
            var embeddings = await generator.GenerateAsync(fragments.Select(f => f.Text), cancellationToken: cancellationToken);

            if (embeddings.Count != fragments.Count)
            {
                throw new InvalidOperationException(
                    $"O provedor devolveu {embeddings.Count} vetores para {fragments.Count} fragmentos.");
            }

            var rows = new List<KnowledgeFragment>(fragments.Count);
            for (var i = 0; i < fragments.Count; i++)
            {
                var vector = embeddings[i].Vector;

                // O provedor pode ACEITAR o pedido de dimensão e ignorá-lo —
                // foi medido que aceita. Conferir aqui, no único lugar onde a
                // dimensão real é conhecida, é o que impede gravar vetor
                // incompatível sem erro nenhum.
                if (vector.Length != options.Dimensions)
                {
                    throw new InvalidOperationException(
                        $"O provedor devolveu embedding com dimensão {vector.Length}, "
                      + $"diferente da dimensão {options.Dimensions} declarada em Embedding:Dimensions. "
                      + "Nenhum fragmento foi gravado.");
                }

                rows.Add(new KnowledgeFragment(
                    document.Id, document.KnowledgeBaseId, fragments[i].Ordinal, fragments[i].Text,
                    new Vector(vector), options.Provider, options.Model, options.Dimensions));
            }

            return await CommitAsync(dbContext, message, rows, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Indexação do documento {DocumentId} falhou na tentativa {Attempt} de {Max}",
                message.KnowledgeDocumentId, message.Attempt, KnowledgeIndexingQueues.MaxAttempts);

            // AQUI estão as duas saídas, e é o que o molde de TaskJobConsumer
            // não cobre: lá o catch é o fim do caminho. As duas já contaram
            // tentativa (MarkAttemptStartedAsync, acima); o que as separa é
            // quem grava Failed.
            if (message.Attempt < KnowledgeIndexingQueues.MaxAttempts)
            {
                return KnowledgeIndexingOutcome.RetryScheduled;
            }

            return await FailAsync(dbContext, message, KnowledgeIndexingFailure.Describe(exception), cancellationToken);
        }
    }

    /// <summary>
    /// Conta a tentativa e move para <c>Indexing</c>, condicionado à revisão.
    /// Não usa o resultado: se zero linhas foram afetadas, a gravação final
    /// também não vai afetar nenhuma e o descarte acontece lá, num lugar só.
    /// </summary>
    private static Task MarkAttemptStartedAsync(
        AppDbContext dbContext, KnowledgeIndexingJobMessage message, DateTimeOffset now, CancellationToken cancellationToken) =>
        dbContext.KnowledgeDocuments
            .Where(d => d.Id == message.KnowledgeDocumentId && d.ContentRevision == message.ContentRevision)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(d => d.IndexingStatus, KnowledgeIndexingStatus.Indexing)
                    .SetProperty(d => d.IndexingAttempts, message.Attempt)
                    .SetProperty(d => d.LastAttemptAt, now),
                cancellationToken);

    /// <summary>
    /// Grava o conjunto novo em <b>transação única</b>: apaga todos os
    /// fragmentos do documento, insere os novos e atualiza o documento —
    /// tudo ou nada (garantia 2 de D9 da etapa 1).
    ///
    /// <para>
    /// A atualização do documento é <b>condicionada à revisão</b> e o código
    /// checa <b>linhas afetadas</b>. Zero linhas significa "a revisão mudou
    /// <b>ou</b> o documento sumiu", e nos dois casos a transação inteira é
    /// revertida — nenhum fragmento gravado, nenhum estado alterado. Não assumir
    /// que o EF lança: a checagem é explícita (design.md, D5).
    /// </para>
    /// </summary>
    private async Task<KnowledgeIndexingOutcome> CommitAsync(
        AppDbContext dbContext,
        KnowledgeIndexingJobMessage message,
        List<KnowledgeFragment> rows,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var affected = await dbContext.KnowledgeDocuments
            .Where(d => d.Id == message.KnowledgeDocumentId && d.ContentRevision == message.ContentRevision)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(d => d.IndexingStatus, KnowledgeIndexingStatus.Indexed)
                    .SetProperty(d => d.IndexedAt, timeProvider.GetUtcNow())
                    .SetProperty(d => d.FragmentCount, rows.Count)
                    .SetProperty(d => d.FailureReason, (string?)null),
                cancellationToken);

        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogInformation(
                "Indexação do documento {DocumentId} descartada: a revisão {Revision} já não é a corrente, ou o documento foi excluído",
                message.KnowledgeDocumentId, message.ContentRevision);
            return KnowledgeIndexingOutcome.Discarded;
        }

        await dbContext.KnowledgeFragments
            .Where(f => f.KnowledgeDocumentId == message.KnowledgeDocumentId)
            .ExecuteDeleteAsync(cancellationToken);

        dbContext.KnowledgeFragments.AddRange(rows);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return KnowledgeIndexingOutcome.Indexed;
    }

    /// <summary>
    /// Única saída que grava <c>Failed</c>. <b>Preserva <c>IndexedAt</c> e os
    /// fragmentos anteriores</b> — é a garantia 3 de D9 da etapa 1, e ela vive
    /// exatamente aqui: o documento continua respondendo com o conteúdo anterior
    /// e a tela mostra que a atualização não pegou.
    ///
    /// <para>
    /// Falhar ao gravar o estado de falha NÃO deixa a mensagem em limbo: a
    /// exceção sobe, o consumidor a trata no seu próprio <c>catch</c> e
    /// confirma a mensagem assim mesmo. O pior caso é o documento ficar em
    /// <c>Indexing</c> até a próxima atualização ou reindexação — visível na
    /// tela, e melhor do que a mensagem voltar para a fila num laço que vai
    /// falhar de novo pelo mesmo motivo (o banco está fora).
    /// </para>
    /// </summary>
    private async Task<KnowledgeIndexingOutcome> FailAsync(
        AppDbContext dbContext, KnowledgeIndexingJobMessage message, string reason, CancellationToken cancellationToken)
    {
        var affected = await dbContext.KnowledgeDocuments
            .Where(d => d.Id == message.KnowledgeDocumentId && d.ContentRevision == message.ContentRevision)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(d => d.IndexingStatus, KnowledgeIndexingStatus.Failed)
                    .SetProperty(d => d.FailureReason, reason),
                cancellationToken);

        if (affected == 0)
        {
            logger.LogInformation(
                "Falha do documento {DocumentId} não foi gravada: a revisão {Revision} já não é a corrente, ou o documento foi excluído",
                message.KnowledgeDocumentId, message.ContentRevision);
            return KnowledgeIndexingOutcome.Discarded;
        }

        return KnowledgeIndexingOutcome.Failed;
    }
}
