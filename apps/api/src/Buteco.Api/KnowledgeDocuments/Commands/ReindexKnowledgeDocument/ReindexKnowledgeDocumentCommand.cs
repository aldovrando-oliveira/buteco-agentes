using Buteco.Api.KnowledgeDocuments.Responses;
using Mediator;

namespace Buteco.Api.KnowledgeDocuments.Commands.ReindexKnowledgeDocument;

/// <summary>
/// Reenfileira a indexação de um documento sem que o conteúdo tenha mudado.
/// <see cref="ReindexKnowledgeDocumentResult.Document"/> é nulo quando o documento não
/// existe, ou existe e pertence a outra base (vira 404 no endpoint). O record de
/// resultado existe desde catalogo-base-sincronizada (D10): a reindexação pode
/// perder duas vezes seguidas para uma edição concorrente do mesmo documento.
/// </summary>
public sealed record ReindexKnowledgeDocumentCommand(Guid KnowledgeBaseId, Guid Id)
    : ICommand<ReindexKnowledgeDocumentResult>;

public sealed record ReindexKnowledgeDocumentResult(KnowledgeDocumentResponse? Document, bool ConcurrentWriteConflict = false);
