using System.Net.Http.Headers;
using Buteco.ProviderCatalog;

namespace Buteco.Api.Providers.Vendors;

/// <summary>
/// <c>GET {OpenAI:BaseUrl}/models</c>, com <c>Authorization: Bearer</c> (operação
/// <c>listModels</c> do <c>openai-openapi</c>, consultado em 19/11/2026). Sem paginação:
/// <c>{ "object": "list", "data": [{ "id", "object", "created", "owned_by" }] }</c>.
///
/// <para>
/// <b>Sem filtro</b> (<see cref="VendorModelFilters.None"/>): a listagem não tem campo que
/// separe conversa de embedding, imagem ou áudio, e filtrar pelo nome seria afirmar o que o
/// fornecedor não disse, e quebraria em gateway compatível (D4).
/// </para>
///
/// <para>
/// O <c>OpenAI:BaseUrl</c> é o mesmo que o <c>apps/workers</c> usa para conversar
/// (<c>ChatClientOptions.BaseUrl</c>, com o mesmo padrão), para listar no endpoint em que o
/// worker conversa (D5). O gateway compatível medido em 19/11/2026 responde <c>200</c> com
/// <c>data: []</c>.
/// </para>
/// </summary>
public sealed class OpenAiModelLister(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IVendorModelLister
{
    public const string DefaultBaseUrl = "https://api.openai.com/v1";

    public string Filter => VendorModelFilters.None;

    public async Task<VendorPage> FetchPageAsync(string apiKey, string? cursor, CancellationToken cancellationToken)
    {
        var baseUrl = configuration["OpenAI:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = DefaultBaseUrl;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var client = httpClientFactory.CreateClient(VendorJson.HttpClientName(LlmProviders.OpenAi));
        var (document, nonSuccess) = await VendorJson.SendAsync(client, request, cancellationToken);
        if (nonSuccess is { } status)
        {
            return VendorPage.Failed(status);
        }

        using (document)
        {
            var models = new List<VendorModel>();
            foreach (var item in VendorJson.RequiredArray(document!.RootElement, "data").EnumerateArray())
            {
                models.Add(new VendorModel(VendorJson.RequiredString(item, "id"), DisplayName: null));
            }

            return new VendorPage(models, NextCursor: null, NonSuccessStatus: null);
        }
    }
}
