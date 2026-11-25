using System.Text.Json.Serialization;

namespace Buteco.Api.Providers.Responses;

// Os nomes no fio vão fixados (convenção 12): são o contrato que a tela da #297 lê, e o
// teste de fio inspeciona o texto do JSON.

/// <summary>Um provedor em <c>GET /providers/models</c> (design.md, D6).</summary>
public sealed record CuratedProviderResponse(
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("configured")] bool Configured,
    [property: JsonPropertyName("models")] IReadOnlyList<CuratedModelResponse> Models);

/// <summary>
/// Um modelo habilitado. <c>agentCount</c> é a contagem MEDIDA dos agentes, ativos ou não,
/// gravados com o par; o zero é resultado da contagem, e pode ser exibido (convenção 13).
/// </summary>
public sealed record CuratedModelResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("agentCount")] int AgentCount);

/// <summary>
/// As opções do fornecedor (D4). <c>filter</c> diz o critério aplicado, e nunca afirma que os
/// modelos são de conversa.
/// </summary>
public sealed record VendorModelsResponse(
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("filter")] string Filter,
    [property: JsonPropertyName("models")] IReadOnlyList<VendorModelResponse> Models);

public sealed record VendorModelResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("enabled")] bool Enabled);

/// <summary>O item habilitado, na resposta de <c>POST /providers/{provider}/models</c>.</summary>
public sealed record EnabledModelResponse(
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("model")] string Model);

/// <summary>O corpo de <c>POST /providers/{provider}/models</c>.</summary>
public sealed record EnableModelRequest(string? Model);
