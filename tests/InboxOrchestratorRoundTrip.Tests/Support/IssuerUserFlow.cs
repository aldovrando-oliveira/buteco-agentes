using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace InboxOrchestratorRoundTrip.Tests.Support;

// Duplicado deliberadamente de tests/ApiConnectorsRoundTrip.Tests/Support/IssuerUserFlow.cs: os projetos de
// tests/ são independentes, sem referência cruzada de código (convenção 11). Qualquer
// mudança aqui precisa ser espelhada lá.

/// <summary>
/// O fluxo real do usuário contra o TestServer do apps/api (design.md da change
/// usuarios-locais-login-emissor, D5): sessão do emissor, authorize com PKCE e troca do
/// código, só por HTTP. O primeiro Admin vem do bootstrap da fixture e o cliente
/// buteco-painel do catálogo. Os dois rodam no aquecimento do apps/api, e o teste os roda
/// de novo antes do login, para não depender do ciclo (parâmetro warmUp).
/// </summary>
public static class IssuerUserFlow
{
    public const string BootstrapAdminEmail = "admin@exemplo.test";

    /// <param name="warmUp">
    /// Roda o bootstrap e o catálogo pelos serviços do apps/api, idempotentes. Sem isso, o
    /// teste dependeria do ciclo do aquecimento, e repetir o login até o bootstrap
    /// concluir não serve: cada tentativa antes dele é uma falha de e-mail sem conta, e a
    /// quinta bloqueia o e-mail por 5 minutos (D4).
    /// </param>
    public static async Task<string> AccessTokenAsync(HttpClient api, Func<Task> warmUp, string publicBaseUrl, string password, string scope)
    {
        await warmUp();
        var redirectUri = new Uri(new Uri(publicBaseUrl), "/entrar/retorno").AbsoluteUri;

        var session = new HttpRequestMessage(HttpMethod.Post, "/auth/session")
        {
            Content = JsonContent.Create(new { email = BootstrapAdminEmail, senha = password }),
        };
        session.Headers.Add("Origin", new Uri(publicBaseUrl).GetLeftPart(UriPartial.Authority));
        var login = await api.SendAsync(session);
        if (login.StatusCode != HttpStatusCode.NoContent)
        {
            throw new InvalidOperationException($"Login falhou: {(int)login.StatusCode} {await login.Content.ReadAsStringAsync()}");
        }

        var cookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var url = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
        {
            ["client_id"] = "buteco-painel",
            ["response_type"] = "code",
            ["scope"] = scope,
            ["redirect_uri"] = redirectUri,
            ["state"] = "estado-do-acordo",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        });

        var authorize = new HttpRequestMessage(HttpMethod.Get, url);
        authorize.Headers.Add("Cookie", cookie);
        var redirect = await api.SendAsync(authorize);
        var location = redirect.Headers.Location;
        if (redirect.StatusCode != HttpStatusCode.Redirect || location is null || !location.IsAbsoluteUri ||
            !QueryHelpers.ParseQuery(location.Query).TryGetValue("code", out var code))
        {
            throw new InvalidOperationException($"Authorize sem código: {(int)redirect.StatusCode} {location}");
        }

        var token = await api.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "buteco-painel",
            ["code"] = code.ToString(),
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = verifier,
        }));
        var body = await token.Content.ReadAsStringAsync();
        if (token.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Troca do código falhou: {(int)token.StatusCode} {body}");
        }

        return JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>O PBKDF2 de Auth:OperatorPasswordHash, o mesmo formato das fixtures do apps/api.</summary>
    public static string LegacyPasswordHash(string password)
    {
        const int iterations = 100_000;
        var salt = "fixed-test-salt-not-for-production"u8.ToArray();
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
