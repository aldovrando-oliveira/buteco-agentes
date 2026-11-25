using System.Net;
using System.Text.Json;

namespace Buteco.Api.Providers.Vendors;

/// <summary>O nome do <c>HttpClient</c> de cada fornecedor (um por provedor, D4); público para os testes trocarem o handler.</summary>
public static class VendorHttpClients
{
    public static string Name(string providerId) => $"VendorModels:{providerId}";
}

/// <summary>
/// O que os três listers fazem igual: mandar a requisição, devolver o status quando não é
/// <c>200</c>, e ler o corpo como JSON, conferindo os campos obrigatórios. Uma forma de
/// leitura, três contratos: cada lister diz quais campos são obrigatórios no dele.
/// </summary>
internal static class VendorJson
{
    public static string HttpClientName(string providerId) => VendorHttpClients.Name(providerId);

    /// <summary>Por página; o orçamento da listagem inteira (30 s) é do catálogo (D4).</summary>
    public static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Manda e lê. Devolve <c>null</c> e o status quando a resposta não é <c>200</c>: o corpo
    /// do fornecedor nunca é lido nem repassado nesse caso (D4: ele pode ecoar a requisição).
    /// </summary>
    public static async Task<(JsonDocument? Document, int? NonSuccessStatus)> SendAsync(
        HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return (null, (int)response.StatusCode);
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            return (await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken), null);
        }
        catch (JsonException)
        {
            throw new VendorContractException("o corpo do 200 não é JSON");
        }
    }

    public static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            throw new VendorContractException($"'{name}' ausente ou não é lista");
        }

        return value;
    }

    public static string RequiredString(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new VendorContractException($"'{name}' ausente ou vazio");
        }

        return value.GetString()!;
    }

    public static string? OptionalString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}
