using Buteco.Api.Infrastructure;
using Buteco.Api.Knowledge.Indexing;
using Buteco.Api.KnowledgeDocuments.Entities;
using Buteco.Api.KnowledgeDocuments.Extraction;
using Buteco.Api.KnowledgeDocuments.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeDocuments.Commands.CreateKnowledgeDocument;

public sealed class CreateKnowledgeDocumentCommandHandler(
    AppDbContext dbContext,
    KnowledgeContentProcessor contentProcessor,
    IKnowledgeIndexingJobPublisher indexingPublisher)
    : ICommandHandler<CreateKnowledgeDocumentCommand, CreateKnowledgeDocumentResult>
{
    public async ValueTask<CreateKnowledgeDocumentResult> Handle(CreateKnowledgeDocumentCommand command, CancellationToken cancellationToken)
    {
        // Base inativa aceita documento normalmente: desativar impede o uso
        // pelo agente, não a manutenção do conteúdo.
        var knowledgeBaseExists = await dbContext.KnowledgeBases
            .AnyAsync(knowledgeBase => knowledgeBase.Id == command.KnowledgeBaseId, cancellationToken);

        if (!knowledgeBaseExists)
        {
            return CreateKnowledgeDocumentResult.KnowledgeBaseNotFound();
        }

        var content = contentProcessor.Process(command.SourceType, command.Content);
        if (!content.Succeeded)
        {
            return CreateKnowledgeDocumentResult.Invalid(content.ValidationErrors!);
        }

        var document = new KnowledgeDocument(
            command.KnowledgeBaseId, command.Title, command.SourceType, content.ExtractedText!);

        dbContext.KnowledgeDocuments.Add(document);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Publica DEPOIS do SaveChanges, nunca antes: uma mensagem publicada
        // antes da gravação apontaria para um documento que o consumidor não
        // acharia, e o SaveChanges ainda pode falhar.
        await indexingPublisher.PublishAsync(
            new KnowledgeIndexingJobMessage(document.Id, document.ContentRevision), cancellationToken);

        // ContentLengthBytes é coluna gerada pelo banco: o EF a lê de volta no
        // próprio SaveChanges, sem Reload() explícito (design.md, D14).
        return CreateKnowledgeDocumentResult.Success(KnowledgeDocumentResponse.FromEntity(document));
    }
}
