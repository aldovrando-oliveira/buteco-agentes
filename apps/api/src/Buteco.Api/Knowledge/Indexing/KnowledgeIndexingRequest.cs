using Buteco.Api.KnowledgeDocuments.Entities;

namespace Buteco.Api.Knowledge.Indexing;

/// <summary>
/// Pedido de indexação ainda não entregue à fila (design.md da change
/// indexacao-sem-job-orfao, D1). É gravado no <b>mesmo</b> <c>SaveChanges</c> do
/// documento por toda escrita que pede indexação, e removido pelo
/// <see cref="KnowledgeIndexingRequestDispatcher"/> só depois de o broker confirmar a
/// mensagem.
///
/// <para>
/// Existe porque <c>Pending</c> não distingue "mensagem na fila" de "publicação que
/// falhou": as duas são a mesma linha do documento. O pedido é o registro de que
/// ainda falta entregar.
/// </para>
/// </summary>
public sealed class KnowledgeIndexingRequest
{
    public Guid Id { get; private set; }

    public Guid KnowledgeDocumentId { get; private set; }

    /// <summary>A revisão que a mensagem vai levar: a do documento na gravação.</summary>
    public int ContentRevision { get; private set; }

    /// <summary>Ordem de despacho e idade do pedido.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    private KnowledgeIndexingRequest()
    {
    }

    /// <summary>
    /// Pedido para o estado em memória do documento. Chamado depois de a escrita
    /// mexer no documento, para levar a revisão já incrementada.
    /// </summary>
    public static KnowledgeIndexingRequest For(KnowledgeDocument document) => new()
    {
        Id = Guid.NewGuid(),
        KnowledgeDocumentId = document.Id,
        ContentRevision = document.ContentRevision,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public KnowledgeIndexingJobMessage ToMessage() => new(KnowledgeDocumentId, ContentRevision);
}
