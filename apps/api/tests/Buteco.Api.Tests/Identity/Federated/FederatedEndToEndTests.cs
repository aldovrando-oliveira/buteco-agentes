using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Identity.Federated;

/// <summary>
/// De ponta a ponta, sem semeador do vínculo (tasks.md, 9.2): o Admin entra por senha, conecta o provedor
/// pela rota com a senha, entra pelo provedor, passa pelo segundo passo e troca o código; o access token é o
/// do login por senha, e <c>/account</c> mostra o vínculo.
/// </summary>
public class FederatedEndToEndTests(FederatedLoginFixture fixture) : IClassFixture<FederatedLoginFixture>
{
    [Fact]
    public async Task ConnectByTheRoute_ThenEnterByTheProvider_WithTheButecoCode()
    {
        var person = await SignedInUser.CreateAsync(fixture, "ponta-a-ponta");
        var account = GoogleFlow.NewAccount(LocalUserSeed.UniqueEmail("ponta-a-ponta-google"));
        var browser = GoogleFlow.Client(fixture);

        // 1. Conectar pela rota, com a senha.
        var connect = new HttpRequestMessage(HttpMethod.Post, "/account/federated/corporativo/connect") { Content = JsonContent.Create(new { senha = person.Password }) };
        connect.Headers.Authorization = new AuthenticationHeaderValue("Bearer", person.AccessToken);
        var started = GoogleFlow.Read(await browser.SendAsync(connect));
        var connected = await GoogleFlow.ReturnAsync(browser, GoogleFlow.Consent(fixture, started, account), started.Query["state"], started.FlowCookie);
        Assert.Equal("/account?conectado=corporativo", connected.Location);

        // 2. Entrar pelo provedor: o retorno manda ao segundo passo, com a sessão pendente.
        await IdentityIssuerUserFlow.EnsurePanelClientAsync(fixture.Services);
        var pkce = IdentityIssuerUserFlow.Pkce.Create();
        var authorize = IdentityIssuerUserFlow.AuthorizeUrl(pkce);
        var returned = await GoogleFlow.LoginAsync(fixture, account, retorno: authorize);
        Assert.Equal("totp", returned.Query["segundoPasso"]);

        // 3. O código de recuperação do cadastro do login por senha completa (o do app do mesmo passo seria reuso).
        var (second, complete) = await TwoFactorTestClient.SecondFactorAsync(browser, returned.Cookie("buteco-sessao"), TwoFactorTestClient.NextRecoveryCode(person.User.Email));
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var code = IdentityIssuerUserFlow.CodeFrom(await IdentityIssuerUserFlow.AuthorizeAsync(browser, returned.Query["retorno"], complete));
        var tokens = await IdentityIssuerUserFlow.ReadTokensAsync(await IdentityIssuerUserFlow.RedeemAsync(browser, code, pkce.Verifier));

        var byProvider = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
        var byPassword = new JwtSecurityTokenHandler().ReadJwtToken(person.AccessToken);
        Assert.Equal(byPassword.Issuer, byProvider.Issuer);
        Assert.Equal(byPassword.Audiences.Order(), byProvider.Audiences.Order());
        Assert.Equal(byPassword.Subject, byProvider.Subject);
        Assert.Equal(
            byPassword.Claims.Single(claim => claim.Type == "client_id").Value,
            byProvider.Claims.Single(claim => claim.Type == "client_id").Value);

        // 4. Minha conta mostra o vínculo.
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var own = await client.GetFromJsonAsync<JsonElement>("/account");
        Assert.Equal(["senha", "google"], own.GetProperty("formasDeEntrada").EnumerateArray().Select(form => form.GetString()));
        Assert.Equal(account.Email, own.GetProperty("contasConectadas")[0].GetProperty("email").GetString());
    }
}
