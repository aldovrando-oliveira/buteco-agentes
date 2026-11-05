using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

/// <summary>
/// O <c>POST /auth/login</c> não existe mais (spec <c>operator-authentication</c>, "Nenhuma
/// rota emite token de operador fora do emissor"; design.md da change
/// painel-entra-pelo-emissor, D8). Sem token, rota inexistente responde <c>401</c>, como
/// qualquer outra: o <c>FallbackPolicy</c> (<c>Program.cs</c>) vale também para o que não
/// casa com rota, e não revela quais existem. A ausência se prova com o token de usuário
/// do emissor: com ele, a rota antiga responderia <c>200</c>, e a ausente responde
/// exatamente <c>404</c>.
/// </summary>
public class GoneLoginTests(LocalUsersFixture factory) : IClassFixture<LocalUsersFixture>
{
    private const string Password = "senha-certa-do-teste";

    // A credencial do Admin do bootstrap: a mesma senha que a rota antiga aceitava.
    private static JsonContent BootstrapCredential() => JsonContent.Create(new
    {
        username = "operator",
        password = ApiFactoryFixture.KnownOperatorPassword,
    });

    private HttpClient Anonymous()
    {
        var client = IdentityIssuerUserFlow.Client(factory);
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }

    [Fact]
    public async Task GoneLogin_WithoutToken_Is401_LikeAnyMissingRoute()
    {
        var response = await Anonymous().PostAsync("/auth/login", BootstrapCredential());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GoneLogin_WithIssuerUserToken_Is404()
    {
        var email = LocalUserSeed.UniqueEmail("gone-login");
        await LocalUserSeed.CreateUserAsync(factory.Services, email, Password);
        var client = Anonymous();
        var tokens = await IdentityIssuerUserFlow.SignInAsync(client, factory.Services, email, Password);

        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login") { Content = BootstrapCredential() };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("token", await response.Content.ReadAsStringAsync());
    }
}
