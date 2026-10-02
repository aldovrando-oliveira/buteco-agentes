namespace Buteco.Api.KnowledgeDocuments.Entities;

/// <summary>
/// O que <see cref="KnowledgeDocument.Update"/> fez, em três respostas que
/// <b>não</b> são a mesma pergunta (design.md da change
/// historico-documentos-base, D4):
///
/// <list type="bullet">
/// <item><see cref="NeedsIndexing"/> é sobre o <b>índice</b>, decidido por
/// <c>ContentHash</c>.</item>
/// <item><see cref="ContentChanged"/> e <see cref="TitleChanged"/> são sobre o
/// <b>documento</b>: o texto e o título mudaram de fato, em comparação ordinal.
/// <see cref="ContentChanged"/> é exatamente a condição que incrementa
/// <c>ContentRevision</c>.</item>
/// </list>
///
/// Divergem na linha legada com <c>ContentHash</c> nulo: reenviar o mesmo texto
/// dá <c>NeedsIndexing = true</c> (nunca indexado sob a regra do hash) e
/// <c>ContentChanged = false</c> (o texto é o mesmo). O histórico usa a segunda.
/// </summary>
public sealed record KnowledgeDocumentUpdateOutcome(bool NeedsIndexing, bool ContentChanged, bool TitleChanged)
{
    /// <summary>A escrita mudou o que o documento é, e por isso gera evento.</summary>
    public bool HasDocumentChange => ContentChanged || TitleChanged;
}
