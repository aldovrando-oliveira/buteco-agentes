namespace Buteco.Api.Knowledge.Indexing;

/// <summary>
/// Pedido de indexação de um documento.
/// </summary>
/// <param name="KnowledgeDocumentId">Documento a indexar.</param>
/// <param name="ContentRevision">
/// Revisão do conteúdo no momento em que o trabalho foi pedido. Viaja na
/// mensagem para que o consumidor saiba **qual** revisão pediu o trabalho e
/// possa descartar o resultado se ela já não for a corrente (design.md, D5) —
/// sem isso ele teria de comparar contra si mesmo e nunca detectaria trabalho
/// obsoleto.
/// </param>
/// <param name="Attempt">
/// Número desta execução, a partir de 1. Viaja na mensagem, e não é lido do
/// documento, porque o contador de re-execução é do **fluxo**: a mensagem
/// republicada na fila de espera carrega o próprio número, e o documento só é
/// tocado quando a execução acontece. Ler do documento faria duas mensagens em
/// voo disputarem o mesmo contador.
/// </param>
public sealed record KnowledgeIndexingJobMessage(
    Guid KnowledgeDocumentId,
    int ContentRevision,
    int Attempt = 1);
