using System.Net;
using System.Security.Cryptography;
using System.Text;
using Buteco.Api.Tests.Support;
using Microsoft.IdentityModel.Tokens;

namespace Buteco.Api.Tests.Identity.Federated;

/// <summary>
/// O duplo do Google testado por si (tasks.md, 4.3): o token confere o PKCE e o código vale uma vez. Sem
/// isso, os guardas do PKCE e do state passariam contra um duplo que aceita tudo.
/// </summary>
public class FakeGoogleTests
{
    private static HttpClient Client(FakeGoogle google)
    {
        var handler = google.CreateHandler();
        handler.InnerHandler = new HttpClientHandler();
        return new HttpClient(handler);
    }

    private static (string Verifier, string Challenge) Pkce()
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (verifier, Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    private static Task<HttpResponseMessage> RedeemAsync(HttpClient client, string code, string verifier) =>
        client.PostAsync(FakeGoogle.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["client_id"] = FakeGoogle.ClientId,
            ["client_secret"] = FakeGoogle.ClientSecret,
        }));

    [Fact]
    public async Task TheRightVerifier_GetsTheIdToken_AndTheCodeWorksOnce()
    {
        var google = new FakeGoogle();
        using var client = Client(google);
        var (verifier, challenge) = Pkce();
        var code = google.Consent(challenge, "nonce-1", new FakeGoogle.Account("sub-1", "a@exemplo.test"));

        var first = await RedeemAsync(client, code, verifier);
        var second = await RedeemAsync(client, code, verifier);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("id_token", await first.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Contains("invalid_grant", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWrongVerifier_IsInvalidGrant()
    {
        var google = new FakeGoogle();
        using var client = Client(google);
        var (_, challenge) = Pkce();
        var code = google.Consent(challenge, "nonce-1", new FakeGoogle.Account("sub-1", "a@exemplo.test"));

        var response = await RedeemAsync(client, code, Pkce().Verifier);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid_grant", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWrongClientSecret_IsInvalidClient()
    {
        var google = new FakeGoogle();
        using var client = Client(google);
        var (verifier, challenge) = Pkce();
        var code = google.Consent(challenge, "nonce-1", new FakeGoogle.Account("sub-1", "a@exemplo.test"));

        var response = await client.PostAsync(FakeGoogle.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["code_verifier"] = verifier,
            ["client_id"] = FakeGoogle.ClientId, ["client_secret"] = "outro",
        }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
