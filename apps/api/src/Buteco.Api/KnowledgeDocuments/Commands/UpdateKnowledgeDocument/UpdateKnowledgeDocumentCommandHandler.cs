using Buteco.Api.Infrastructure;
using Buteco.Api.Knowledge.Indexing;
using Buteco.Api.KnowledgeBases.Entities;
using Buteco.Api.KnowledgeDocuments.Entities;
using Buteco.Api.KnowledgeDocuments.Extraction;
using Buteco.Api.KnowledgeDocuments.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeDocuments.Commands.UpdateKnowledgeDocument;

public sealed class UpdateKnowledgeDocumentCommandHandler(
    AppDbContext dbContext,
    KnowledgeContentProcessor contentProcessor,
    KnowledgeIndexingRequestDispatcher indexingDispatcher,
    ILogger<UpdateKnowledgeDocumentCommandHandler> logger)
    : ICommandHandler<UpdateKnowledgeDocumentCommand, UpdateKnowledgeDocumentResult>
{
    /// <summary>
    /// Evento do log da atualização relida e reaplicada depois de uma
    /// <see cref="DbUpdateConcurrencyException"/> (catalogo-base-sincronizada, D10).
    /// Público para o teste de corrida contar quantas vezes o caminho rodou.
    /// </summary>
    public static readonly EventId ConcurrentUpdateRetriedEvent = new(1023, "ConcurrentOperatorDocumentUpdateRetried");

    public async ValueTask<UpdateKnowledgeDocumentResult> Handle(UpdateKnowledgeDocumentCommand command, CancellationToken cancellationToken)
    {
        var document = await FindAsync(command, cancellationToken);

        if (document is null)
        {
            return UpdateKnowledgeDocumentResult.NotFound();
        }

        // Em base sincronizada os documentos vêm da pasta (catalogo-base-sincronizada,
        // D7). A cópia do tipo no documento é igual ao tipo da base, garantido pela
        // FK composta (D4), então não precisa ler a base.
        if (document.KnowledgeBaseContentMode == KnowledgeBaseContentMode.Synced)
        {
            return UpdateKnowledgeDocumentResult.SyncedKnowledgeBase();
        }

        // Validação antes de tocar a entidade: conteúdo inválido ou acima do
        // teto deixa documento, revisão e estado exatamente como estavam.
        var content = contentProcessor.Process(command.SourceType, command.Content);
        if (!content.Succeeded)
        {
            return UpdateKnowledgeDocumentResult.Invalid(content.Refusal!);
        }

        var extractedText = content.ExtractedText!;

        // ContentRevision é token de concorrência (catalogo-base-sincronizada, D10).
        // Duas edições que leram a mesma revisão não gravam mais as duas N+1: a
        // segunda falha com DbUpdateConcurrencyException, e aqui ela é relida e
        // reaplicada UMA vez. O defeito que isto fecha existia antes da #102: as duas
        // publicavam a mesma revisão para textos diferentes.
        try
        {
            return await ApplyOnceAsync(document, command, extractedText, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation(
                ConcurrentUpdateRetriedEvent,
                "Atualização concorrente do documento {DocumentId}: relido e reaplicado.",
                command.Id);
            dbContext.ChangeTracker.Clear();
        }

        var current = await FindAsync(command, cancellationToken);
        if (current is null)
        {
            // Excluído entre as duas tentativas.
            return UpdateKnowledgeDocumentResult.NotFound();
        }

        try
        {
            return await ApplyOnceAsync(current, command, extractedText, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Segunda falha seguida: nada gravado, e a escrita pode ser repetida
            // (D10). Nunca 500.
            dbContext.ChangeTracker.Clear();
            return UpdateKnowledgeDocumentResult.ConcurrentWrite();
        }
    }

    private Task<KnowledgeDocument?> FindAsync(UpdateKnowledgeDocumentCommand command, CancellationToken cancellationToken) =>
        // Filtra por KnowledgeBaseId E Id, nunca só por Id: sem isso seria
        // possível editar documento de outra base pelo caminho errado.
        dbContext.KnowledgeDocuments.FirstOrDefaultAsync(
            document => document.Id == command.Id && document.KnowledgeBaseId == command.KnowledgeBaseId,
            cancellationToken);

    private async Task<UpdateKnowledgeDocumentResult> ApplyOnceAsync(
        KnowledgeDocument document, UpdateKnowledgeDocumentCommand command, string extractedText, CancellationToken cancellationToken)
    {
        // Update devolve se HÁ conteúdo novo a indexar. Atualização que só
        // troca o título preserva o estado de indexação e NÃO enfileira — é a
        // regra do ContentHash (design.md, D9), e é o que impede gastar
        // embedding à toa em edição de metadado.
        var outcome = document.Update(command.Title, command.SourceType, extractedText);

        // Evento só quando o DOCUMENTO mudou (texto ou título), e não quando o
        // índice precisa de trabalho: na linha legada com hash nulo os dois
        // divergem (historico-documentos-base, D4). Mesmo SaveChanges da
        // escrita (D2).
        if (outcome.HasDocumentChange)
        {
            dbContext.KnowledgeDocumentEvents.Add(KnowledgeDocumentEvent.Updated(document, outcome, command.Author));
        }

        // Pedido de indexação no mesmo SaveChanges (indexacao-sem-job-orfao, D1), com a
        // revisão já incrementada. A tentativa que perde para a concorrência não grava:
        // o ChangeTracker.Clear() do chamador solta o pedido junto com o resto.
        var indexingRequest = outcome.NeedsIndexing ? KnowledgeIndexingRequest.For(document) : null;
        if (indexingRequest is not null)
        {
            dbContext.KnowledgeIndexingRequests.Add(indexingRequest);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (indexingRequest is not null)
        {
            await indexingDispatcher.DispatchAfterWriteAsync([indexingRequest.Id], cancellationToken);
        }

        return UpdateKnowledgeDocumentResult.Success(KnowledgeDocumentResponse.FromEntity(document));
    }
}
