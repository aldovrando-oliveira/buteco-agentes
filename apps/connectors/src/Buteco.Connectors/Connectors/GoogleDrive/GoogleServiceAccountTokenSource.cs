using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Buteco.Connectors.Connectors.GoogleDrive;

/// <summary>
/// O <c>access_token</c> da service account, por OAuth sem biblioteca (design.md, D2):
/// JWT <c>RS256</c> assinado com a chave privada e trocado em
/// <c>https://oauth2.googleapis.com/token</c>
/// (developers.google.com/identity/protocols/oauth2/service-account, lida em 03/10/2026).
/// </summary>
/// <remarks>
/// O token fica em memória até <see cref="RefreshMargin"/> antes do fim do
/// <c>expires_in</c>, e um <see cref="SemaphoreSlim"/> impede duas trocas em paralelo.
/// Nem o token nem a asserção aparecem em log.
/// </remarks>
public sealed class GoogleServiceAccountTokenSource(
    GoogleServiceAccountKey key,
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    ILogger<GoogleServiceAccountTokenSource> logger)
{
    public const string Scope = "https://www.googleapis.com/auth/drive.readonly";

    internal static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    // O máximo que o Google aceita entre iat e exp.
    private static readonly TimeSpan AssertionLifetime = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _refreshAt;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is { } cached && timeProvider.GetUtcNow() < _refreshAt)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            if (_accessToken is { } current && now < _refreshAt)
            {
                return current;
            }

            var (token, expiresIn) = await ExchangeAsync(now, cancellationToken);
            _accessToken = token;
            _refreshAt = now + expiresIn - RefreshMargin;
            return token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(string Token, TimeSpan ExpiresIn)> ExchangeAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = CreateAssertion(now),
        });

        HttpResponseMessage response;
        try
        {
            var client = httpClientFactory.CreateClient(GoogleDriveHttp.OAuthClientName);
            response = await client.PostAsync(GoogleDriveHttp.TokenEndpoint, content, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Troca de token do Google falhou por rede ou timeout ({ExceptionType}).", exception.GetType().Name);
            throw new ConnectorFailure(ConnectorCodes.ProviderUnavailable);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // O corpo de erro do OAuth ({"error","error_description"}) não traz a
                // chave, mas também não vai para o log: só o status.
                logger.LogWarning("Troca de token do Google recusada com {StatusCode}.", (int)response.StatusCode);
                throw new ConnectorFailure((int)response.StatusCode >= 500
                    ? ConnectorCodes.ProviderUnavailable
                    : ConnectorCodes.ProviderAuthFailed);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            var token = root.GetProperty("access_token").GetString()
                ?? throw new ConnectorFailure(ConnectorCodes.ProviderAuthFailed);
            var expiresIn = root.TryGetProperty("expires_in", out var seconds) && seconds.TryGetInt32(out var value)
                ? TimeSpan.FromSeconds(value)
                : AssertionLifetime;
            return (token, expiresIn);
        }
    }

    internal string CreateAssertion(DateTimeOffset now)
    {
        var header = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>
        {
            ["alg"] = "RS256",
            ["typ"] = "JWT",
            ["kid"] = key.PrivateKeyId,
        });
        var claims = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = key.ClientEmail,
            ["scope"] = Scope,
            ["aud"] = GoogleDriveHttp.TokenEndpoint.AbsoluteUri,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.Add(AssertionLifetime).ToUnixTimeSeconds(),
        });

        var signingInput = $"{Base64Url(header)}.{Base64Url(claims)}";
        using var rsa = key.CreateRsa();
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
