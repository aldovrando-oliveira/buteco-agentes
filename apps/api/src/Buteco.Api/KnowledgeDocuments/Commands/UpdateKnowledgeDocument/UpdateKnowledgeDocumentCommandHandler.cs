using Buteco.Api.Infrastructure;
using Buteco.Api.Knowledge.Indexing;
using Buteco.Api.KnowledgeDocuments.Entities;
using Buteco.Api.KnowledgeDocuments.Extraction;
using Buteco.Api.KnowledgeDocuments.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeDocuments.Commands.UpdateKnowledgeDocument;

public sealed class UpdateKnowledgeDocumentCommandHandler(
    AppDbContext dbContext,
    KnowledgeContentProcessor contentProcessor,
    IKnowledgeIndexingJobPublisher indexingPublisher)
    : ICommandHandler<UpdateKnowledgeDocumentCommand, UpdateKnowledgeDocumentResult>
{
    public async ValueTask<UpdateKnowledgeDocumentResult> Handle(UpdateKnowledgeDocumentCommand command, CancellationToken cancellationToken)
    {
        // Filtra por KnowledgeBaseId E Id, nunca só por Id: sem isso seria
        // possível editar documento de outra base pelo caminho errado.
        var document = await dbContext.KnowledgeDocuments
            .FirstOrDefaultAsync(
                document => document.Id == command.Id && document.KnowledgeBaseId == command.KnowledgeBaseId,
                cancellationToken);

        if (document is null)
        {
            return UpdateKnowledgeDocumentResult.NotFound();
        }

        // Validação antes de tocar a entidade: conteúdo inválido ou acima do
        // teto deixa documento, revisão e estado exatamente como estavam.
        var content = contentProcessor.Process(command.SourceType, command.Content);
        if (!content.Succeeded)
        {
            return UpdateKnowledgeDocumentResult.Invalid(content.ValidationErrors!);
        }

        // Update devolve se HÁ conteúdo novo a indexar. Atualização que só
        // troca o título preserva o estado de indexação e NÃO enfileira — é a
        // regra do ContentHash (design.md, D9), e é o que impede gastar
        // embedding à toa em edição de metadado.
        var outcome = document.Update(command.Title, command.SourceType, content.ExtractedText!);

        // Evento só quando o DOCUMENTO mudou (texto ou título), e não quando o
        // índice precisa de trabalho: na linha legada com hash nulo os dois
        // divergem (historico-documentos-base, D4). Mesmo SaveChanges da
        // escrita (D2).
        if (outcome.HasDocumentChange)
        {
            dbContext.KnowledgeDocumentEvents.Add(KnowledgeDocumentEvent.Updated(document, outcome, command.Author));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (outcome.NeedsIndexing)
        {
            await indexingPublisher.PublishAsync(
                new KnowledgeIndexingJobMessage(document.Id, document.ContentRevision), cancellationToken);
        }

        return UpdateKnowledgeDocumentResult.Success(KnowledgeDocumentResponse.FromEntity(document));
    }
}
