using System.Text.Json.Serialization;

namespace Buteco.Api.KnowledgeDocuments.Entities;

/// <summary>
/// O que uma escrita fez com o documento (design.md da change
/// historico-documentos-base, D1). Gravado como string no banco e no fio, nunca
/// como ordinal: acrescentar um valor não renumera os outros.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<KnowledgeDocumentEventType>))]
public enum KnowledgeDocumentEventType
{
    Created,
    Updated,
    Deleted,
}
