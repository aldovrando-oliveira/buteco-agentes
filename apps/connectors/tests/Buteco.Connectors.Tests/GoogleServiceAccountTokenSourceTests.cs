using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Buteco.Connectors.Connectors;
using Buteco.Connectors.Connectors.GoogleDrive;
using Buteco.Connectors.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Buteco.Connectors.Tests;

// google-drive-connector, "Autenticação no Google por JWT da service account".
public class GoogleServiceAccountTokenSourceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly TestServiceAccountKey _key = TestServiceAccountKey.Create();
    private readonly GoogleDriveFakeHandler _google = new();
    private readonly ManualTimeProvider _time = new(Start);

    private GoogleServiceAccountTokenSource CreateSource()
    {
        var services = new ServiceCollection();
        services.AddHttpClient(GoogleDriveHttp.OAuthClientName).ConfigurePrimaryHttpMessageHandler(() => _google);
        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        return new GoogleServiceAccountTokenSource(
            GoogleServiceAccountKey.Parse(_key.Base64), factory, _time, NullLogger<GoogleServiceAccountTokenSource>.Instance);
    }

    [Fact]
    public async Task Assertion_TemCabecalhoClaimsEAssinaturaVerificavel()
    {
        var source = CreateSource();

        await source.GetAccessTokenAsync(CancellationToken.None);

        var request = Assert.Single(_google.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        var form = HttpUtility.ParseQueryString(request.Body!);
        Assert.Equal("urn:ietf:params:oauth:grant-type:jwt-bearer", form["grant_type"]);

        var parts = form["assertion"]!.Split('.');
        Assert.Equal(3, parts.Length);

        var header = JsonDocument.Parse(Base64UrlDecode(parts[0])).RootElement;
        Assert.Equal("RS256", header.GetProperty("alg").GetString());
        Assert.Equal("JWT", header.GetProperty("typ").GetString());
        Assert.Equal(_key.PrivateKeyId, header.GetProperty("kid").GetString());

        var claims = JsonDocument.Parse(Base64UrlDecode(parts[1])).RootElement;
        Assert.Equal(TestServiceAccountKey.ClientEmail, claims.GetProperty("iss").GetString());
        Assert.Equal("https://www.googleapis.com/auth/drive.readonly", claims.GetProperty("scope").GetString());
        Assert.Equal("https://oauth2.googleapis.com/token", claims.GetProperty("aud").GetString());
        var iat = claims.GetProperty("iat").GetInt64();
        var exp = claims.GetProperty("exp").GetInt64();
        Assert.Equal(Start.ToUnixTimeSeconds(), iat);
        Assert.InRange(exp - iat, 1, 3600);

        var signed = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        Assert.True(_key.Rsa.VerifyData(signed, Base64UrlDecode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public async Task DuasOperacoesEmSequencia_UmaTrocaSo()
    {
        var source = CreateSource();

        var first = await source.GetAccessTokenAsync(CancellationToken.None);
        var second = await source.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(1, _google.TokenExchanges);
    }

    [Fact]
    public async Task OperacoesConcorrentes_UmaTrocaSo()
    {
        var source = CreateSource();

        var tokens = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => source.GetAccessTokenAsync(CancellationToken.None)));

        Assert.Single(tokens.Distinct());
        Assert.Equal(1, _google.TokenExchanges);
    }

    [Fact]
    public async Task PertoDoFimDaValidade_TrocaDeNovo()
    {
        var source = CreateSource();
        await source.GetAccessTokenAsync(CancellationToken.None);

        _time.Advance(TimeSpan.FromSeconds(3599) - TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        await source.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal(2, _google.TokenExchanges);
    }

    [Fact]
    public async Task AindaLongeDoFim_NaoTroca()
    {
        var source = CreateSource();
        await source.GetAccessTokenAsync(CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(50));
        await source.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal(1, _google.TokenExchanges);
    }

    [Fact]
    public async Task TrocaRecusadaComInvalidGrant_ProviderAuthFailed()
    {
        _google.On(r => r.RequestUri!.AbsoluteUri == GoogleDriveFakeHandler.TokenUrl, _ =>
            GoogleDriveFakeHandler.Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Invalid JWT Signature."}"""));
        var source = CreateSource();

        var failure = await Assert.ThrowsAsync<ConnectorFailure>(() => source.GetAccessTokenAsync(CancellationToken.None));

        Assert.Equal(ConnectorCodes.ProviderAuthFailed, failure.Code);
    }

    [Fact]
    public async Task EndpointDeTokenFora_ProviderUnavailable()
    {
        _google.On(r => r.RequestUri!.AbsoluteUri == GoogleDriveFakeHandler.TokenUrl, _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var source = CreateSource();

        var failure = await Assert.ThrowsAsync<ConnectorFailure>(() => source.GetAccessTokenAsync(CancellationToken.None));

        Assert.Equal(ConnectorCodes.ProviderUnavailable, failure.Code);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }
}
