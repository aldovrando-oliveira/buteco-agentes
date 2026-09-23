using Buteco.Workers.EmbeddingMetrics;
using Buteco.Workers.EmbeddingMetrics.Entities;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Knowledge.Chunking;
using Buteco.Workers.Knowledge.Embedding;
using Buteco.Workers.Knowledge.Entities;
using Buteco.Workers.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
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
    /// <summary>
    /// A unidade de trabalho, com a <b>coleta de embedding</b> em volta (change
    /// <c>metricas-embedding-coleta</c>).
    ///
    /// <para>
    /// <b>A escrita da métrica acontece DEPOIS do estado do documento</b>
    /// (design.md, D7), e <see cref="EmbeddingMetricsWriter"/> nunca lança: não
    /// há ordem de execução em que ela altere o
    /// <see cref="KnowledgeIndexingOutcome"/> que o consumidor recebe.
    /// </para>
    ///
    /// <para>
    /// <b>Se <see cref="IndexCoreAsync"/> lançar, não há métrica a gravar</b>, e
    /// isso é escolha e não esquecimento: o único jeito de ele lançar é o banco
    /// estar fora na gravação do estado de falha, ou o cancelamento de shutdown
    /// — e nos dois casos a gravação da métrica falharia pelo mesmo motivo. É o
    /// custo de crash que D7 nomeia e aceita.
    /// </para>
    /// </summary>
    public async Task<KnowledgeIndexingOutcome> IndexAsync(KnowledgeIndexingJobMessage message, CancellationToken cancellationToken)
    {
        var context = new KnowledgeIndexingAttemptContext(
            message.KnowledgeDocumentId,
            message.ContentRevision,
            message.Attempt,
            KnowledgeIndexingQueues.MaxAttempts,
            timeProvider.GetUtcNow());

        var outcome = await IndexCoreAsync(context, message, cancellationToken);

        await new EmbeddingMetricsWriter(scopeFactory, logger)
            .WriteAsync(context, outcome, timeProvider.GetUtcNow());

        return outcome;
    }

    private async Task<KnowledgeIndexingOutcome> IndexCoreAsync(
        KnowledgeIndexingAttemptContext context, KnowledgeIndexingJobMessage message, CancellationToken cancellationToken)
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
        context.KnowledgeBaseId = document.KnowledgeBaseId;

        var now = timeProvider.GetUtcNow();
        await MarkAttemptStartedAsync(dbContext, message, now, cancellationToken);

        // A tentativa está contada — e é a partir daqui que existe linha de
        // tentativa a gravar (design.md, D8). O descarte acima retornou antes, e
        // não gastou chamada ao gateway: é o que mantém a contagem de linhas
        // reconciliável com IndexingAttempts do documento.
        context.MarkCounted();

        try
        {
            context.Phase = EmbeddingMetricsValues.FailurePhase.Chunking;
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

            context.Phase = EmbeddingMetricsValues.FailurePhase.ProviderResolution;
            var generator = embeddingResolver.Resolve();

            context.Phase = EmbeddingMetricsValues.FailurePhase.EmbeddingGateway;
            var embeddings = await GenerateInBatchesAsync(generator, fragments, options, context, cancellationToken);

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
                    context.Phase = EmbeddingMetricsValues.FailurePhase.DimensionMismatch;
                    throw new InvalidOperationException(
                        $"O provedor devolveu embedding com dimensão {vector.Length}, "
                      + $"diferente da dimensão {options.Dimensions} declarada em Embedding:Dimensions. "
                      + "Nenhum fragmento foi gravado.");
                }

                rows.Add(new KnowledgeFragment(
                    document.Id, document.KnowledgeBaseId, fragments[i].Ordinal, fragments[i].Text,
                    new Vector(vector), options.Provider, options.Model, options.Dimensions));
            }

            context.FragmentCount = rows.Count;
            context.Phase = EmbeddingMetricsValues.FailurePhase.Persistence;
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
    /// Gera o embedding dos fragmentos em <b>lotes sequenciais</b>, devolvendo os
    /// vetores na ordem dos fragmentos.
    ///
    /// <para>
    /// <b>POR QUE EXISTE.</b> Até 20/09/2026 esta chamada era uma só, com todos
    /// os fragmentos do documento, e nada impunha teto. Medido no piloto de
    /// 20/09/2026 (gateway de embedding do <c>.env.prod</c>, modelo de 4.096
    /// dimensões): o documento <c>02 HISTORICO E STATUS</c>, com ~442
    /// fragmentos, falhou com <c>502 upstream_error</c> nas <b>três</b>
    /// tentativas — inclusive <b>isolado</b>, sem outra indexação concorrendo —,
    /// enquanto as duas metades dele (267 e 175 fragmentos) indexaram, e o
    /// documento <c>01</c> (78 fragmentos) indexou sempre.
    /// </para>
    ///
    /// <para>
    /// <b>O formato do teto do gateway NÃO foi estabelecido</b> — por número de
    /// entradas, por bytes do corpo, ou por tempo de resposta do upstream. O que
    /// se sabe é o par: 442 falha, 267 passa. Por isso o tamanho do lote é
    /// configuração, e não constante: variá-lo é como o teto vai ser descoberto
    /// (ver <c>EmbeddingOptions.BatchSize</c>).
    /// </para>
    ///
    /// <para>
    /// <b>SEQUENCIAL, e a medida é que decide.</b> O mesmo documento <c>01</c>,
    /// trabalho idêntico, variou <b>4,4×</b> em duração entre duas indexações
    /// (0,83 s contra 3,62 s) com uma chamada só: o upstream é instável, e a
    /// variação não vem daqui. Disparar lotes concorrentes contra um upstream
    /// assim aumenta a chance de uma das chamadas estar perto do limite na hora
    /// errada — e não há medição nenhuma que diga que o gateway aguenta N
    /// chamadas simultâneas. A medida 1 aponta para o outro lado: o documento
    /// falhou <b>isolado</b>. Concorrência aqui precisa de motivo medido.
    /// </para>
    ///
    /// <para>
    /// <b>SÓ a chamada ao gateway é loteada.</b> Os vetores de todos os lotes são
    /// acumulados e gravados <b>uma vez</b>, na transação única de
    /// <see cref="CommitAsync"/> — a substituição integral dos fragmentos
    /// continua sendo tudo ou nada (garantia 2 de D9 da etapa 1). Fracionar a
    /// gravação abriria uma janela em que o documento tem parte dos fragmentos
    /// novos e parte dos antigos, e a busca devolveria conteúdo de duas revisões
    /// misturado, sem erro nenhum.
    /// </para>
    ///
    /// <para>
    /// <b>E A RETENTATIVA CONTINUA SENDO DO DOCUMENTO INTEIRO.</b> Uma falha no
    /// segundo lote reprocessa o primeiro — custo real, aceito. Retentar por
    /// lote é exatamente a alternativa que D3 de <c>knowledge-base-indexacao</c>
    /// recusou (*"Não há retry da chamada ao provedor dentro da execução. Uma
    /// camada só, um contador só, um significado só."*), e ela quebraria o que
    /// <c>KnowledgeIndexingQueues.MaxAttempts</c> afirma — *"uma execução é uma
    /// tentativa, não uma chamada HTTP"* —, que é o que faz a tela de documentos
    /// poder dizer "três tentativas, a última às 03:14" sem mentir. Lote menor
    /// reduz o desperdício por ser menos trabalho a repetir, e sobretudo por
    /// falhar menos.
    /// </para>
    /// </summary>
    private static async Task<List<Embedding<float>>> GenerateInBatchesAsync(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        IReadOnlyList<ChunkedFragment> fragments,
        EmbeddingOptions options,
        KnowledgeIndexingAttemptContext context,
        CancellationToken cancellationToken)
    {
        var vectors = new List<Embedding<float>>(fragments.Count);

        // A MEDIDA ENTRA AQUI, no sítio de chamada, e NÃO dentro de
        // EmbeddingGeneratorResolver.Resolve() (design.md da change
        // metricas-embedding-coleta, D4). O motivo é de verificação: os duplos
        // dos testes substituem o RESOLVEDOR, então embrulhar lá faria todo
        // cenário de teste passar por fora da medição, e os guardas ficariam
        // verdes sem nada medido.
        //
        // Uma linha por CHAMADA — e como a chamada é o lote, um documento
        // grande produz N linhas. É esse grão que torna o 502 de um lote
        // específico consultável (D3).
        var measured = new MeasuredEmbeddingGenerator(
            generator,
            measurement => context.Record(EmbeddingCall.ForIndexing(
                context.AttemptId,
                context.KnowledgeBaseId,
                options.Provider,
                options.Model,
                options.Dimensions,
                measurement)));

        foreach (var batch in fragments.Chunk(options.BatchSize))
        {
            var texts = batch.Select(f => f.Text).ToList();
            var generated = await measured.GenerateAsync(texts, cancellationToken: cancellationToken);

            // Conferência POR LOTE, e não só no total: aqui a informação é
            // local, e a mensagem pode dizer qual lote divergiu. O total fica
            // implicado — se todo lote casa, a soma casa.
            if (generated.Count != texts.Count)
            {
                context.Phase = EmbeddingMetricsValues.FailurePhase.VectorCountMismatch;
                throw new InvalidOperationException(
                    $"O provedor devolveu {generated.Count} vetores para {texts.Count} fragmentos "
                  + $"no lote começando no fragmento {vectors.Count}.");
            }

            vectors.AddRange(generated);
        }

        return vectors;
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
