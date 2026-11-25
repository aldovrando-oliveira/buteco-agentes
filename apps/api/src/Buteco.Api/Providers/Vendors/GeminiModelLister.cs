using System.Text.Json;
using Buteco.ProviderCatalog;

namespace Buteco.Api.Providers.Vendors;

/// <summary>
/// <c>GET https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000</c>, paginado
/// por <c>pageToken</c> enquanto houver <c>nextPageToken</c>
/// (<c>aistudio.google.com/api-reference/models/list-models</c>, consultado em 19/11/2026).
///
/// <para>
/// <b>A chave vai em <c>x-goog-api-key</c>, nunca em <c>?key=</c></b> (o exemplo REST de
/// <c>aistudio.google.com/docs/api-key</c>; D5, convenção 3).
/// </para>
///
/// <para>
/// <b>Só os itens cujo <c>supportedGenerationMethods</c> contém <c>generateContent</c></b>, o
/// que <b>não</b> é "de conversa": há modelos que geram conteúdo e não conversam (D4). O
/// <c>id</c> é o <c>name</c> sem o prefixo <c>models/</c>, que é o que o <c>Google.GenAI</c>
/// do <c>apps/workers</c> recebe como modelo; <c>name</c> sem o prefixo é resposta fora do
/// contrato.
/// </para>
/// </summary>
public sealed class GeminiModelLister(IHttpClientFactory httpClientFactory) : IVendorModelLister
{
    public const string GenerateContent = "generateContent";

    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models";

    private const string NamePrefix = "models/";

    public string Filter => VendorModelFilters.SupportsGenerateContent;

    public async Task<VendorPage> FetchPageAsync(string apiKey, string? cursor, CancellationToken cancellationToken)
    {
        var url = $"{Endpoint}?pageSize=1000";
        if (cursor is not null)
        {
            url += $"&pageToken={Uri.EscapeDataString(cursor)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-goog-api-key", apiKey);

        var client = httpClientFactory.CreateClient(VendorJson.HttpClientName(LlmProviders.Gemini));
        var (document, nonSuccess) = await VendorJson.SendAsync(client, request, cancellationToken);
        if (nonSuccess is { } status)
        {
            return VendorPage.Failed(status);
        }

        using (document)
        {
            var root = document!.RootElement;
            var models = new List<VendorModel>();

            // A documentação diz que "models" vem na resposta; uma página sem ele é tratada como
            // vazia só se também não houver próxima página. Com cursor e sem a lista, é fora do
            // contrato.
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("models", out _))
            {
                foreach (var item in VendorJson.RequiredArray(root, "models").EnumerateArray())
                {
                    var name = VendorJson.RequiredString(item, "name");
                    if (!name.StartsWith(NamePrefix, StringComparison.Ordinal) || name.Length == NamePrefix.Length)
                    {
                        throw new VendorContractException("'name' fora do formato models/{id}");
                    }

                    if (SupportsGenerateContent(item))
                    {
                        models.Add(new VendorModel(name[NamePrefix.Length..], VendorJson.OptionalString(item, "displayName")));
                    }
                }
            }

            var next = VendorJson.OptionalString(root, "nextPageToken");
            if (next is not null && !root.TryGetProperty("models", out _))
            {
                throw new VendorContractException("página com 'nextPageToken' e sem 'models'");
            }

            return new VendorPage(models, next, NonSuccessStatus: null);
        }
    }

    private static bool SupportsGenerateContent(JsonElement item) =>
        item.TryGetProperty("supportedGenerationMethods", out var methods) &&
        methods.ValueKind == JsonValueKind.Array &&
        methods.EnumerateArray().Any(method => method.ValueKind == JsonValueKind.String && method.GetString() == GenerateContent);
}
