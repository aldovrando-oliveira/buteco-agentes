using System.Text.Json.Serialization;

namespace Buteco.Workers.Knowledge.Entities;

/// <summary>
/// Espelho de <c>Buteco.Api.KnowledgeDocuments.Entities.KnowledgeIndexingStatus</c>
/// — duplicado de propósito, sem <c>ProjectReference</c> cruzado e sem lib
/// compartilhada, mesmo padrão já usado para <c>McpServerAuthType</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<KnowledgeIndexingStatus>))]
public enum KnowledgeIndexingStatus
{
    Pending,
    Indexing,
    Indexed,
    Failed,
}
