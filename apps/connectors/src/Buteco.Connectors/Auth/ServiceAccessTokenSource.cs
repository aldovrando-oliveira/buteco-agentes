using System.Text.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Buteco.Connectors.Auth;

// Duplicado deliberadamente de
// apps/inbox/src/Buteco.Inbox/Auth/ServiceAccessTokenSource.cs — sem libs/
// (design.md da change servicos-token-emissor, D3, e remover-hmac-token-servico, D10).
// Qualquer mudança aqui precisa ser espelhada lá, e vice-versa; o que difere de propósito é
// o client_id, em ServiceClientOptions. Cada cópia é provada contra o emissor real em tests/.

/// <summary>
/// O token de serviço do <c>apps/connectors</c>, obtido no emissor do <c>apps/api</c> (spec
/// <c>service-client-credentials</c>; design.md da change servicos-token-emissor, D3).
/// <list type="bullet">
/// <item><b>Pedido:</b> <c>POST {Api:BaseUrl}/connect/token</c>, pelo endereço interno, num
/// cliente nomeado sem o <see cref="ServiceTokenDelegatingHandler"/>, com o formulário
/// codificado pela biblioteca e <c>scope=buteco-api</c>, sem o qual o token sai sem
/// <c>aud</c> (H10).</item>
/// <item><b>Cache:</b> o token serve até faltar <see cref="ServiceAccessTokenSchedule.RenewalMargin"/>
/// para vencer, contado pelo <c>expires_in</c> e pelo relógio local na chegada.</item>
/// <item><b>Pedido único:</b> o semáforo só é tomado quando o cache não serve, e o cache é
/// conferido de novo depois de entrar.</item>
/// <item><b>Falha:</b> toda falha do emissor sai como <see cref="HttpRequestException"/>, que
/// cada chamador já trata como "<c>apps/api</c> indisponível" (H13). Durante
/// <see cref="ServiceAccessTokenSchedule.FailedRequestBackoff"/>, os pedidos falham na hora,
/// sem HTTP. Nenhum log ecoa o segredo nem o <c>Api:BaseUrl</c>.</item>
/// </list>
/// </summary>
public sealed class ServiceAccessTokenSource(
    IHttpClientFactory httpClientFactory,
    IOptions<ServiceClientOptions> options,
    ServiceAccessTokenSchedule schedule,
    TimeProvider timeProvider,
    ILogger<ServiceAccessTokenSource> logger)
{
    public const string HttpClientName = "IssuerToken";
    public const string TokenPath = "/connect/token";
    public const string Scope = "buteco-api";

    private readonly SemaphoreSlim _request = new(1, 1);
    private readonly Lock _gate = new();
    private CachedToken? _cached;
    private DateTimeOffset? _lastFailureAt;
    private bool _announce = true;

    public async ValueTask<string> GetAsync(CancellationToken cancellationToken)
    {
        if (TryCached() is { } cached)
        {
            return cached;
        }

        await _request.WaitAsync(cancellationToken);
        try
        {
            // Quem esperou o semáforo usa o token que o pedido anterior trouxe.
            if (TryCached() is { } obtained)
            {
                return obtained;
            }

            lock (_gate)
            {
                if (_lastFailureAt is { } failedAt && timeProvider.GetUtcNow() - failedAt < schedule.FailedRequestBackoff)
                {
                    throw new HttpRequestException(
                        $"Token de serviço {ServiceClientOptions.ClientId} suspenso até {failedAt + schedule.FailedRequestBackoff:O}: o pedido anterior falhou.");
                }
            }

            return await RequestAsync(cancellationToken);
        }
        finally
        {
            _request.Release();
        }
    }

    /// <summary>
    /// Descarta o token em cache se ele ainda for <paramref name="token"/>: um <c>401</c>
    /// atrasado de um token já trocado não derruba o novo.
    /// </summary>
    public void Invalidate(string token)
    {
        lock (_gate)
        {
            if (_cached?.Token == token)
            {
                _cached = null;
            }
        }
    }

    private string? TryCached()
    {
        lock (_gate)
        {
            return _cached is { } cached && timeProvider.GetUtcNow() < cached.ExpiresAt - schedule.RenewalMargin
                ? cached.Token
                : null;
        }
    }

    private async Task<string> RequestAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(schedule.RequestTimeout);

        HttpResponseMessage response;
        try
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = ServiceClientOptions.ClientId,
                ["client_secret"] = options.Value.Secret ?? string.Empty,
                ["scope"] = Scope,
            });
            response = await httpClientFactory.CreateClient(HttpClientName).PostAsync(TokenPath, form, timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw Fail("timeout", invalidClient: false);
        }
        catch (HttpRequestException exception)
        {
            throw Fail($"emissor inalcançável ({exception.HttpRequestError})", invalidClient: false);
        }

        using (response)
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw Fail("timeout", invalidClient: false);
            }

            var (accessToken, expiresIn, error) = Read(body);
            if (!response.IsSuccessStatusCode)
            {
                throw error == "invalid_client"
                    ? Fail("invalid_client", invalidClient: true)
                    : Fail($"{(int)response.StatusCode}{(error is null ? string.Empty : " " + error)}", invalidClient: false);
            }

            if (string.IsNullOrEmpty(accessToken) || expiresIn is not > 0)
            {
                throw Fail("resposta sem access_token ou sem expires_in", invalidClient: false);
            }

            var now = timeProvider.GetUtcNow();
            var expiresAt = now + TimeSpan.FromSeconds(expiresIn.Value);
            bool announce;
            lock (_gate)
            {
                _cached = new CachedToken(accessToken, expiresAt);
                _lastFailureAt = null;
                announce = _announce;
                _announce = false;
            }

            // Linha do primeiro token e do primeiro depois de uma falha: é por ela que a
            // conferência pós-deploy vê a credencial aceita (D8). Nunca o token.
            if (announce)
            {
                logger.LogInformation(
                    "Token de serviço {ClientId} obtido no emissor pelo Api:BaseUrl ({TokenPath}), válido até {ExpiresAt:O}.",
                    ServiceClientOptions.ClientId,
                    TokenPath,
                    expiresAt);
            }

            return accessToken;
        }
    }

    private HttpRequestException Fail(string reason, bool invalidClient)
    {
        lock (_gate)
        {
            _lastFailureAt = timeProvider.GetUtcNow();
            _announce = true;
        }

        // O invalid_client não passa sozinho: é segredo divergente entre este serviço e o
        // apps/api, e por isso é Error. Sem o segredo e sem o Api:BaseUrl.
        if (invalidClient)
        {
            logger.LogError(
                "Token de serviço {ClientId} recusado pelo emissor ({TokenPath}): invalid_client. "
              + "ServiceClient:Secret deste app difere do segredo do cliente no apps/api.",
                ServiceClientOptions.ClientId,
                TokenPath);
        }
        else
        {
            logger.LogWarning(
                "Token de serviço {ClientId} não obtido no emissor ({TokenPath}): {Reason}.",
                ServiceClientOptions.ClientId,
                TokenPath,
                reason);
        }

        return new HttpRequestException($"Token de serviço {ServiceClientOptions.ClientId} indisponível: {reason}.");
    }

    private static (string? AccessToken, double? ExpiresIn, string? Error) Read(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null, null);
            }

            return (
                root.TryGetProperty("access_token", out var token) && token.ValueKind == JsonValueKind.String ? token.GetString() : null,
                root.TryGetProperty("expires_in", out var expires) && expires.ValueKind == JsonValueKind.Number ? expires.GetDouble() : null,
                root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String ? error.GetString() : null);
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAt);
}

/// <summary>Registro do token de serviço, com o cliente nomeado do pedido.</summary>
public static class ServiceAccessTokenRegistration
{
    public static IServiceCollection AddServiceAccessToken(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ServiceClientOptions>(configuration.GetSection(ServiceClientOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new ServiceAccessTokenSchedule());
        services.AddSingleton<ServiceAccessTokenSource>();

        // Sem o ServiceTokenDelegatingHandler, para não pedir token para pedir token. O
        // prazo é o do schedule, no próprio pedido; o do cliente fica de fora.
        // Registrado sempre, como o SyncApiClient: sem Api:BaseUrl ninguém o chama, porque o
        // ciclo não roda e o "Sincronizar agora" responde sync-not-configured antes (D3).
        services.AddHttpClient(ServiceAccessTokenSource.HttpClientName, (provider, client) =>
        {
            var api = provider.GetRequiredService<IOptions<Buteco.Connectors.Options.ApiOptions>>().Value;
            if (api.IsConfigured)
            {
                client.BaseAddress = new Uri(api.BaseUrl!);
            }

            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        // Transient: AddHttpMessageHandler<T> não registra o handler no contêiner.
        services.AddTransient<ServiceTokenDelegatingHandler>();
        return services;
    }
}
