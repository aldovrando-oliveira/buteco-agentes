using Buteco.Api.Infrastructure;
using Buteco.Api.Knowledge.Indexing;
using Buteco.Api.KnowledgeDocuments.Entities;
using Buteco.Api.KnowledgeDocuments.Extraction;
using Buteco.Api.KnowledgeSync.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Buteco.Api.KnowledgeSync.Commands.UpsertSyncedDocument;

/// <summary>
/// Upsert de documento de base sincronizada por <c>ExternalRef</c> (design.md da
/// change catalogo-base-sincronizada, D3, D8 e D10). As regras de normalização,
/// indexação e histórico são as mesmas da escrita do operador: o mesmo
/// <see cref="KnowledgeContentProcessor"/>, as mesmas fábricas de evento, e
/// <see cref="KnowledgeDocument.ApplyExternalRevision"/> delegando a
/// <see cref="KnowledgeDocument.Update"/> quando há mudança.
/// </summary>
public sealed class UpsertSyncedDocumentCommandHandler(
    AppDbContext dbContext,
    KnowledgeContentProcessor contentProcessor,
    IKnowledgeIndexingJobPublisher indexingPublisher,
    ILogger<UpsertSyncedDocumentCommandHandler> logger)
    : ICommandHandler<UpsertSyncedDocumentCommand, UpsertSyncedDocumentResult>
{
    /// <summary>Nome do índice único parcial de <c>(KnowledgeBaseId, ExternalRef)</c>.</summary>
    private const string ExternalRefIndexName = "IX_knowledge_documents_KnowledgeBaseId_ExternalRef";

    /// <summary>
    /// Evento do log que registra a corrida recuperada (D10). Público para o teste de
    /// corrida contar quantas vezes o caminho de <c>UniqueViolation</c> rodou: corrida
    /// que nunca viu a corrida não prova nada.
    /// </summary>
    public static readonly EventId ConcurrentUpsertRecoveredEvent = new(1021, "ConcurrentSyncedDocumentUpsertRecovered");

    /// <summary>
    /// Evento do log que registra a atualização relida e reaplicada depois de uma
    /// <see cref="DbUpdateConcurrencyException"/> (D10, token de concorrência). Público
    /// pelo mesmo motivo de <see cref="ConcurrentUpsertRecoveredEvent"/>.
    /// </summary>
    public static readonly EventId ConcurrentUpdateRetriedEvent = new(1022, "ConcurrentSyncedDocumentUpdateRetried");

    public async ValueTask<UpsertSyncedDocumentResult> Handle(UpsertSyncedDocumentCommand command, CancellationToken cancellationToken)
    {
        // Base inativa aceita o upsert: desativar impede o uso pelo agente, não a
        // manutenção do conteúdo.
        var lookup = await dbContext.LookupSyncedKnowledgeBaseAsync(command.KnowledgeBaseId, cancellationToken);
        if (lookup != SyncedKnowledgeBaseLookup.Synced)
        {
            return UpsertSyncedDocumentResult.NotSynced(lookup);
        }

        // Validação antes de tocar a entidade: conteúdo inválido ou acima do teto
        // deixa o documento existente como estava, inclusive o marcador.
        var content = contentProcessor.Process(command.SourceType, command.Content);
        if (!content.Succeeded)
        {
            return UpsertSyncedDocumentResult.Invalid(content.ValidationErrors!);
        }

        var extractedText = content.ExtractedText!;

        var existing = await FindAsync(command, cancellationToken);
        if (existing is not null)
        {
            return await ApplyToExistingAsync(existing, command, extractedText, cancellationToken);
        }

        var document = KnowledgeDocument.CreateSynced(
            command.KnowledgeBaseId, command.Title, command.SourceType, extractedText, command.ExternalRef, command.ExternalVersion);

        // Mesmo SaveChanges do documento (historico-documentos-base, D2).
        dbContext.KnowledgeDocuments.Add(document);
        dbContext.KnowledgeDocumentEvents.Add(KnowledgeDocumentEvent.Created(document, command.Author));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsExternalRefUniqueViolation(exception))
        {
            // Outro upsert da mesma referência venceu entre a leitura e este
            // SaveChanges (D10). Detach das entidades Added desta tentativa
            // (documento e evento) e UMA releitura, não um laço: o Postgres só
            // libera a violação para quem perde depois que o vencedor fez commit,
            // então o vencedor já está gravado. É o idioma de ContactSessionResolver
            // (apps/inbox). O log é a única evidência observável de que a corrida
            // aconteceu, e é o que o teste de corrida conta.
            logger.LogInformation(
                ConcurrentUpsertRecoveredEvent,
                "Upsert concorrente de documento sincronizado: a base {KnowledgeBaseId} já tinha a referência {ExternalRef} gravada por outra requisição; aplicado como atualização.",
                command.KnowledgeBaseId,
                command.ExternalRef);
            DetachAddedEntities();

            var winner = await FindAsync(command, cancellationToken)
                ?? throw new InvalidOperationException(
                    "Violação de unicidade de ExternalRef sem documento vencedor na releitura.", exception);

            return await ApplyToExistingAsync(winner, command, extractedText, cancellationToken);
        }

        // Publica DEPOIS do SaveChanges que deu certo, nunca antes.
        await indexingPublisher.PublishAsync(
            new KnowledgeIndexingJobMessage(document.Id, document.ContentRevision), cancellationToken);

        return UpsertSyncedDocumentResult.Success(document.Id, SyncedDocumentUpsertOutcome.Created);
    }

    private Task<KnowledgeDocument?> FindAsync(UpsertSyncedDocumentCommand command, CancellationToken cancellationToken) =>
        // Comparação como veio (ordinal no banco, collation determinística): 'AbC' e
        // 'abc' são documentos diferentes.
        dbContext.KnowledgeDocuments.FirstOrDefaultAsync(
            document => document.KnowledgeBaseId == command.KnowledgeBaseId && document.ExternalRef == command.ExternalRef,
            cancellationToken);

    /// <summary>
    /// Atualização com <c>ContentRevision</c> como token de concorrência (D10). Se outra
    /// escrita gravou o documento entre a leitura e o <c>SaveChanges</c>, o
    /// <c>UPDATE</c> não acha a revisão lida e o EF lança
    /// <see cref="DbUpdateConcurrencyException"/>: o handler limpa o change tracker,
    /// relê e reaplica UMA vez, e o perdedor fica com a revisão seguinte à do
    /// vencedor, publicando a própria indexação. Uma segunda falha seguida não vira
    /// 500: responde que a escrita concorrente impediu a gravação e que ela pode ser
    /// repetida.
    /// </summary>
    private async Task<UpsertSyncedDocumentResult> ApplyToExistingAsync(
        KnowledgeDocument document, UpsertSyncedDocumentCommand command, string extractedText, CancellationToken cancellationToken)
    {
        try
        {
            return await ApplyOnceAsync(document, command, extractedText, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation(
                ConcurrentUpdateRetriedEvent,
                "Atualização concorrente do documento sincronizado {ExternalRef} da base {KnowledgeBaseId}: relido e reaplicado.",
                command.ExternalRef,
                command.KnowledgeBaseId);
            dbContext.ChangeTracker.Clear();
        }

        var current = await FindAsync(command, cancellationToken);
        if (current is null)
        {
            // Excluído entre as duas tentativas: não há o que reaplicar sem decidir
            // por conta própria recriar o documento. O conector repete.
            return UpsertSyncedDocumentResult.ConcurrentWrite();
        }

        try
        {
            return await ApplyOnceAsync(current, command, extractedText, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return UpsertSyncedDocumentResult.ConcurrentWrite();
        }
    }

    private async Task<UpsertSyncedDocumentResult> ApplyOnceAsync(
        KnowledgeDocument document, UpsertSyncedDocumentCommand command, string extractedText, CancellationToken cancellationToken)
    {
        // Título, tipo de origem e texto iguais: só o marcador é gravado, sem
        // evento, sem indexação e sem tocar UpdatedAt (D3).
        var outcome = document.ApplyExternalRevision(command.Title, command.SourceType, extractedText, command.ExternalVersion);

        if (outcome.HasDocumentChange)
        {
            dbContext.KnowledgeDocumentEvents.Add(KnowledgeDocumentEvent.Updated(document, outcome, command.Author));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Publica DEPOIS do SaveChanges que deu certo: a falha de concorrência
        // acontece antes daqui, e a tentativa perdida não publica nada.
        if (outcome.NeedsIndexing)
        {
            await indexingPublisher.PublishAsync(
                new KnowledgeIndexingJobMessage(document.Id, document.ContentRevision), cancellationToken);
        }

        return UpsertSyncedDocumentResult.Success(
            document.Id,
            outcome.HasDocumentChange ? SyncedDocumentUpsertOutcome.Updated : SyncedDocumentUpsertOutcome.Unchanged);
    }

    private void DetachAddedEntities()
    {
        foreach (var entry in dbContext.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Added).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    // Só a violação DESTE índice é corrida de upsert; qualquer outra sobe.
    private static bool IsExternalRefUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ExternalRefIndexName,
        };
}
