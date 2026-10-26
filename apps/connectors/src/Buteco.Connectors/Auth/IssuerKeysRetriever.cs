using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Buteco.Connectors.Auth;

// Duplicado deliberadamente de
// apps/inbox/src/Buteco.Inbox/Auth/IssuerKeysRetriever.cs
// — sem libs/ (design.md da change validacao-jwks-inbox-connectors, D10). Qualquer
// mudança aqui precisa ser espelhada lá, e vice-versa; o que difere de propósito é a
// audience e a tabela de clientes.

/// <summary>
/// Busca as chaves do emissor pelo endereço interno do <c>apps/api</c> (change
/// validacao-jwks-inbox-connectors, D3 e D4). É o <c>IConfigurationRetriever</c> do
/// <c>ConfigurationManager</c> do esquema do emissor, que cuida do cache, da renovação
/// em segundo plano e do limite de busca por <c>kid</c> desconhecido.
/// <para>
/// <b>Endereço interno:</b> lê <c>{Api:BaseUrl}/.well-known/openid-configuration</c> só
/// para conferir o <c>issuer</c>, e <c>{Api:BaseUrl}/.well-known/jwks</c> para as chaves.
/// O <c>jwks_uri</c> anunciado no discovery é o público e nunca é seguido; o caminho é
/// fixo pela spec <c>oidc-issuer</c> e conferido no boot do <c>apps/api</c>. O
/// <c>address</c> e o <c>IDocumentRetriever</c> que o <c>ConfigurationManager</c> passa
/// são ignorados: o <c>HttpClient</c> vem do <c>IHttpClientFactory</c> a cada busca,
/// porque o endereço do contêiner do <c>apps/api</c> muda a cada recriação. Os logs
/// nomeiam a chave <c>Api:BaseUrl</c> e o caminho, e não ecoam o valor, que pode carregar
/// credencial (o mesmo cuidado de <c>ValidateApiConfiguration</c> do <c>apps/connectors</c>).
/// </para>
/// <para>
/// <b>Espera entre falhas:</b> antes da primeira obtenção, o <c>ConfigurationManager</c>
/// busca na própria requisição e sem backoff (F1). Dentro de
/// <see cref="IssuerKeysSchedule.FailedFetchBackoff"/> depois de uma falha, a busca
/// falha na hora, sem HTTP: com o <c>apps/api</c> fora, uma rajada custa no máximo uma
/// busca por intervalo, e cada requisição responde sem esperar o timeout.
/// </para>
/// </summary>
public sealed class IssuerKeysRetriever(
    IHttpClientFactory httpClientFactory,
    IOptions<IssuerTokenOptions> issuerOptions,
    IssuerKeysState state,
    IssuerKeysSchedule schedule,
    TimeProvider timeProvider,
    ILogger<IssuerKeysRetriever> logger) : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    public const string HttpClientName = "IssuerKeys";
    public const string DiscoveryPath = "/.well-known/openid-configuration";
    public const string JwksPath = "/.well-known/jwks";

    private readonly Lock _gate = new();
    private DateTimeOffset? _lastFailureAt;

    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        lock (_gate)
        {
            if (_lastFailureAt is { } failedAt && timeProvider.GetUtcNow() - failedAt < schedule.FailedFetchBackoff)
            {
                throw new InvalidOperationException(
                    $"Busca das chaves do emissor suspensa até {failedAt + schedule.FailedFetchBackoff:O}: a anterior falhou.");
            }
        }

        try
        {
            var configuration = await FetchAsync(cancel);
            state.MarkObtained();
            // Linha de sucesso: é por ela que a conferência pós-deploy vê a obtenção pelo
            // endereço interno (docs/deployment.md).
            logger.LogInformation(
                "Chaves do emissor obtidas pelo Api:BaseUrl ({JwksPath}): kids {Kids}.",
                JwksPath,
                // Distinct: o JWKS do OpenIddict traz n/e e x5c, e GetSigningKeys devolve
                // uma chave para cada, com o mesmo kid.
                string.Join(", ", configuration.SigningKeys.Select(key => key.KeyId).Distinct()));
            return configuration;
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _lastFailureAt = timeProvider.GetUtcNow();
            }

            if (exception is IssuerMismatchException)
            {
                logger.LogError(exception, "{Message}", exception.Message);
            }
            else
            {
                logger.LogWarning(
                    "Busca das chaves do emissor pelo Api:BaseUrl ({DiscoveryPath}, {JwksPath}) falhou: {Reason}",
                    DiscoveryPath,
                    JwksPath,
                    exception.Message);
            }

            throw;
        }
    }

    private async Task<OpenIdConnectConfiguration> FetchAsync(CancellationToken cancel)
    {
        var expectedIssuer = issuerOptions.Value.NormalizedIssuer;
        var client = httpClientFactory.CreateClient(HttpClientName);

        using (var discovery = JsonDocument.Parse(await client.GetStringAsync(DiscoveryPath, cancel)))
        {
            var announcedIssuer = discovery.RootElement.TryGetProperty("issuer", out var issuer) && issuer.ValueKind == JsonValueKind.String
                ? issuer.GetString()
                : null;
            if (!string.Equals(announcedIssuer, expectedIssuer, StringComparison.Ordinal))
            {
                throw new IssuerMismatchException(
                    $"O discovery do emissor lido pelo Api:BaseUrl declara o issuer '{announcedIssuer}', "
                  + $"diferente de Identity:Issuer '{expectedIssuer}'. Nenhuma chave foi obtida. "
                  + "Identity:Issuer deste app e PublicUrl:BaseUrl do apps/api precisam ter o mesmo valor.");
            }
        }

        var jwks = new JsonWebKeySet(await client.GetStringAsync(JwksPath, cancel));
        var signingKeys = jwks.GetSigningKeys();
        if (signingKeys.Count == 0)
        {
            throw new InvalidOperationException("O JWKS do emissor lido pelo Api:BaseUrl não tem chave de assinatura.");
        }

        var configuration = new OpenIdConnectConfiguration { Issuer = expectedIssuer, JsonWebKeySet = jwks };
        foreach (var key in signingKeys)
        {
            configuration.SigningKeys.Add(key);
        }

        return configuration;
    }

    private sealed class IssuerMismatchException(string message) : Exception(message);
}
