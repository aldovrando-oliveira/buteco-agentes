using System.Text.Json.Serialization;

namespace Buteco.Api.KnowledgeDocuments.Entities;

/// <summary>
/// Estado de indexação de um <see cref="KnowledgeDocument"/>.
/// </summary>
/// <remarks>
/// String no fio desde o primeiro dia (convenção 12, mesmo padrão de
/// <c>McpServerAuthType</c>), e string também no banco
/// (<c>HasConversion&lt;string&gt;()</c>) — nunca inteiro ordinal.
///
/// São exatamente quatro valores. NÃO existe valor separado para reindexação
/// (design.md, D8): a distinção entre "nunca indexado" e "há conteúdo indexado
/// respondendo agora" é carregada por <see cref="KnowledgeDocument.IndexedAt"/>,
/// nulo no primeiro caso e preenchido no segundo, em qualquer um dos quatro
/// estados. Um valor <c>Reindexing</c> não bastaria, porque o ciclo tem dois
/// estados não-terminais e a reindexação passa pelos dois com fragmentos
/// antigos vivos — seriam dois valores novos para expressar o que
/// <see cref="KnowledgeDocument.IndexedAt"/> já responde.
///
/// Nesta etapa só <see cref="Pending"/> é escrito: não há fila nem consumidor
/// de indexação (design.md, D10). Os outros três nascem sem escritor, de
/// propósito — são contrato, e quem os escreve é o consumidor da etapa de
/// indexação.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<KnowledgeIndexingStatus>))]
public enum KnowledgeIndexingStatus
{
    Pending,
    Indexing,
    Indexed,
    Failed,
}
