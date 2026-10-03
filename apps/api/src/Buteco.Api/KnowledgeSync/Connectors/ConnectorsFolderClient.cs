using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Buteco.Api.KnowledgeSync.Connectors;

/// <summary>
/// Chama <c>GET /connectors/providers/{provider}/folder?id=</c> no
/// <c>apps/connectors</c> (design.md da change criacao-base-sincronizada, D2, D3 e
/// D7). O token <c>service:api</c> é assinado pelo
/// <see cref="Auth.ServiceTokenDelegatingHandler"/> do <c>HttpClient</c> nomeado.
///
/// <para>
/// <b>Nunca lança por falha do outro app.</b> Sem resposta é
/// <c>connectors-unavailable</c> (503); resposta fora do contrato, inclusive 401 e
/// 403, é <c>connectors-error</c> (502) com o status recebido no detalhe; erro com
/// código no formato passa como veio, com o status que o <c>apps/connectors</c> deu,
/// trocando só 404 por 422. Só o cancelamento do próprio chamador propaga.
/// </para>
/// </summary>
public sealed class ConnectorsFolderClient(
    IHttpClientFactory httpClientFactory,
    IOptions<ConnectorsOptions> options,
    ILogger<ConnectorsFolderClient> logger) : IConnectorsFolderClient
{
    public const string HttpClientName = "ConnectorsFolderClient";

    /// <summary>
    /// Fixo, sem configuração (D2), e amarrado a dois números de outros lugares:
    /// ACIMA dos 30 s por chamada de metadado do apps/connectors
    /// (GoogleDriveHttp.MetadataTimeout, em
    /// apps/connectors/src/Buteco.Connectors/Connectors/GoogleDrive/GoogleDriveHttp.cs),
    /// para que o código mais preciso dele (provider-unavailable) chegue ao cliente
    /// quando o Google demora; e ABAIXO dos 60 s padrão de proxy_read_timeout do nginx
    /// do painel (apps/frontend/deploy/nginx.conf não fixa a diretiva), para o painel
    /// receber o código daqui e não um 504 do proxy. Mudar um dos três exige rever os
    /// outros dois.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(35);

    public async Task<ConnectorsFolderResult> DescribeFolderAsync(
        string provider, string folderId, CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured)
        {
            return ConnectorsFolderResult.Failure(StatusCodes.Status503ServiceUnavailable, ConnectorsFailureCodes.NotConfigured);
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        var path = $"/connectors/providers/{Uri.EscapeDataString(provider)}/folder?id={Uri.EscapeDataString(folderId)}";

        ConnectorsFolderResult result;
        try
        {
            using var response = await client.GetAsync(path, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            result = response.StatusCode == HttpStatusCode.OK
                ? ReadFolder(body, folderId)
                : ReadFailure(response.StatusCode, body);
        }
        catch (HttpRequestException)
        {
            result = ConnectorsFolderResult.Failure(StatusCodes.Status503ServiceUnavailable, ConnectorsFailureCodes.Unavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout do HttpClient, e não cancelamento do chamador: o .NET representa
            // os dois como TaskCanceledException, e só o segundo deve propagar (mesmo
            // tratamento de AgentReferenceValidator, apps/inbox).
            result = ConnectorsFolderResult.Failure(StatusCodes.Status503ServiceUnavailable, ConnectorsFailureCodes.Unavailable);
        }

        if (!result.Succeeded)
        {
            logger.LogWarning(
                "Validação de pasta no apps/connectors recusada: provedor {Provider}, status {Status}, código {Code}.",
                provider,
                result.FailureStatus,
                result.FailureCode);
        }

        return result;
    }

    // 200 dentro do contrato: id, name e webUrl não vazios, webUrl absoluta http(s), e
    // o id IGUAL ao pedido (D7). Fora disso a base acompanharia uma pasta diferente da
    // escolhida, ou guardaria um link que não abre.
    private static ConnectorsFolderResult ReadFolder(string body, string requestedFolderId)
    {
        var outOfContract = ConnectorsFolderResult.Failure(
            StatusCodes.Status502BadGateway, ConnectorsFailureCodes.Error, "200");

        if (!TryParseObject(body, out var root))
        {
            return outOfContract;
        }

        var id = ReadString(root, "id");
        var name = ReadString(root, "name");
        var webUrl = ReadString(root, "webUrl");

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(webUrl) ||
            !string.Equals(id, requestedFolderId, StringComparison.Ordinal) ||
            !ConnectorsConfigurationValidation.IsAbsoluteHttpUri(webUrl))
        {
            return outOfContract;
        }

        return ConnectorsFolderResult.Success(new ConnectorsFolderDescription(id, name, webUrl));
    }

    // Erro com código no formato e status da tabela da D3 passa como veio; 404
    // (provider-not-configured) vira 422, porque num POST de coleção 404 diria que a
    // rota não existe. O resto é connectors-error com o status recebido.
    private static ConnectorsFolderResult ReadFailure(HttpStatusCode status, string body)
    {
        var mappedStatus = status switch
        {
            HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity => StatusCodes.Status422UnprocessableEntity,
            HttpStatusCode.BadGateway => StatusCodes.Status502BadGateway,
            HttpStatusCode.ServiceUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => (int?)null,
        };

        if (mappedStatus is not null && TryParseObject(body, out var root))
        {
            var code = ReadString(root, "code");
            if (SyncCode.IsValid(code))
            {
                // O apps/connectors omite "detail" quando não há detalhe (tarefa 1.2).
                return ConnectorsFolderResult.Failure(mappedStatus.Value, code!, ReadString(root, "detail"));
            }
        }

        return ConnectorsFolderResult.Failure(
            StatusCodes.Status502BadGateway, ConnectorsFailureCodes.Error, ((int)status).ToString());
    }

    private static bool TryParseObject(string body, out JsonElement root)
    {
        root = default;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
