using Buteco.Api.KnowledgeDocuments.Responses;
using Mediator;

namespace Buteco.Api.KnowledgeDocuments.Commands.ReindexKnowledgeDocument;

/// <summary>
/// Reenfileira a indexação de um documento sem que o conteúdo tenha mudado.
/// Devolve <c>null</c> quando o documento não existe, ou existe e pertence a
/// outra base (vira 404 no endpoint) — não há caso de validação, e por isso não
/// há record de resultado próprio, ao contrário de
/// <c>UpdateKnowledgeDocumentCommand</c>.
/// </summary>
public sealed record ReindexKnowledgeDocumentCommand(Guid KnowledgeBaseId, Guid Id)
    : ICommand<KnowledgeDocumentResponse?>;
