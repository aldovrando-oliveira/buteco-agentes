using System.Text.Json.Serialization;
using Buteco.Connectors.Connectors;

namespace Buteco.Connectors.Endpoints;

// Formato de fio (convenção 12; design.md, D9): nomes fixados explicitamente, e o tipo
// da pasta como string. accountEmail e webUrl têm maiúscula no meio e não dependem da
// naming policy.

public sealed record ProviderResponse(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("accountEmail")] string AccountEmail);

public sealed record FolderEntryResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("webUrl")] string WebUrl)
{
    public static FolderEntryResponse From(FolderEntry entry) => new(entry.Id, entry.Name, entry.Kind.ToString(), entry.WebUrl);
}

public sealed record FolderDescriptionResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("webUrl")] string WebUrl);
