using System.Text.Json;
using Buteco.ProviderCatalog;

namespace Buteco.Api.Providers.Vendors;

/// <summary>
/// <c>GET https://api.anthropic.com/v1/models?limit=1000&amp;lifecycle=active</c>, paginado
/// por <c>after_id</c> enquanto <c>has_more</c> (<c>platform.claude.com/docs/en/api/models/list</c>,
/// consultado em 19/11/2026).
///
/// <para>
/// <b>Cabeçalhos: <c>x-api-key</c> e <c>anthropic-version: 2023-06-01</c></b>, o mesmo par
/// que o pacote <c>Anthropic</c> 12.39.0 do <c>apps/workers</c> manda
/// (<c>Anthropic.Core/ParamsBase.cs</c>, decompilado): a listagem e a conversa falham juntas,
/// se falharem (D5).
/// </para>
///
/// <para>
/// <b>O critério <c>active</c> é aplicado duas vezes</b>: no parâmetro e no campo
/// <c>lifecycle</c> de cada item, para não depender de o servidor honrar o parâmetro com a
/// versão <c>2023-06-01</c>. Item sem o campo torna a resposta inteira fora do contrato: sem
/// ele não há como saber se o modelo está ativo (D4, R12).
/// </para>
/// </summary>
public sealed class AnthropicModelLister(IHttpClientFactory httpClientFactory) : IVendorModelLister
{
    public const string ApiVersion = "2023-06-01";

    private const string Endpoint = "https://api.anthropic.com/v1/models";

    public string Filter => VendorModelFilters.LifecycleActive;

    public async Task<VendorPage> FetchPageAsync(string apiKey, string? cursor, CancellationToken cancellationToken)
    {
        var url = $"{Endpoint}?limit=1000&lifecycle=active";
        if (cursor is not null)
        {
            url += $"&after_id={Uri.EscapeDataString(cursor)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", ApiVersion);

        var client = httpClientFactory.CreateClient(VendorJson.HttpClientName(LlmProviders.Anthropic));
        var (document, nonSuccess) = await VendorJson.SendAsync(client, request, cancellationToken);
        if (nonSuccess is { } status)
        {
            return VendorPage.Failed(status);
        }

        using (document)
        {
            var root = document!.RootElement;
            var models = new List<VendorModel>();
            foreach (var item in VendorJson.RequiredArray(root, "data").EnumerateArray())
            {
                var id = VendorJson.RequiredString(item, "id");
                var lifecycle = VendorJson.RequiredString(item, "lifecycle");
                if (lifecycle == "active")
                {
                    models.Add(new VendorModel(id, VendorJson.OptionalString(item, "display_name")));
                }
            }

            if (!root.TryGetProperty("has_more", out var hasMore) ||
                hasMore.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new VendorContractException("'has_more' ausente");
            }

            string? next = null;
            if (hasMore.GetBoolean())
            {
                next = VendorJson.RequiredString(root, "last_id");
            }

            return new VendorPage(models, next, NonSuccessStatus: null);
        }
    }
}
