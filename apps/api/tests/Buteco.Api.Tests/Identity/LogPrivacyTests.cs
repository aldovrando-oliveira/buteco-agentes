using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users.TwoFactor;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// Nenhuma linha de log, de nenhuma categoria capturada (as do Buteco e as do OpenIddict, servidor e cliente),
/// leva dado pessoal ou segredo, fluxo por fluxo (spec <c>federated-login</c>, "Logs do login federado sem dado
/// pessoal", e <c>issuer-user-login</c>; design.md da change aceite-convite-google, D11; issue #277).
///
/// <para>
/// O host é o real, com o <c>appsettings.json</c>: o nível que a captura recebe é o da configuração. Sem o
/// filtro do OpenIddict no <c>appsettings.json</c>, o login pelo Google, o aceite e o conectar reprovam pela
/// linha <c>OpenIddictClientDispatcher[6195]</c> (o userinfo com o <c>sub</c> e o e-mail), guarda verificado
/// contra o defeito (tasks.md, 11.4).
/// </para>
/// </summary>
public abstract class LogPrivacyFlows<TFixture>(TFixture fixture) : IClassFixture<TFixture>
    where TFixture : FederatedLoginFixture
{
    private const string Panel = "/entrar/retorno";

    /// <summary>Os valores que não podem aparecer em linha nenhuma, com o tipo de cada um.</summary>
    private sealed class Watched
    {
        private readonly List<(string Kind, string Value)> _values = [];

        public void Add(string kind, string? value)
        {
            if (!string.IsNullOrEmpty(value) && value.Length >= 6)
            {
                _values.Add((kind, value));
            }
        }

        public IReadOnlyList<(string Kind, string Value)> Values => _values;
    }

    [Fact]
    public async Task PasswordLogin_Enrollment_Code_Renewal_AndLogout_LogNoPersonalDataNorSecret()
    {
        var watched = new Watched();
        var before = fixture.Logs.Lines.Count;
        var client = GoogleFlow.Client(fixture);
        var name = $"Pessoa Privada {Guid.NewGuid():N}"[..28];
        var email = LocalUserSeed.UniqueEmail("log-senha");
        const string password = "senha-do-caso-de-log-1";
        await LocalUserSeed.CreateUserAsync(fixture.Services, email, password, name);
        watched.Add("e-mail", email);
        watched.Add("nome", name);
        watched.Add("senha", password);

        // Cadastro das 2 etapas no login.
        var (first, pending) = await TwoFactorTestClient.PasswordStepAsync(client, email, password);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var enrolled = await TwoFactorTestClient.EnrollAsync(client, fixture.Services, pending!);
        watched.Add("segredo das 2 etapas", Base32.Encode(enrolled.Secret));
        watched.Add("código das 2 etapas", Totp.Compute(enrolled.Secret, Totp.StepAt(fixture.Services.GetRequiredService<TimeProvider>().GetUtcNow())));
        foreach (var code in enrolled.RecoveryCodes)
        {
            watched.Add("código de recuperação", code);
        }

        var tokens = await TokensAsync(client, enrolled.SessionCookie, watched);

        // O login seguinte, com o código das 2 etapas (o de recuperação: o do app do mesmo passo seria reuso).
        var (again, pendingAgain) = await TwoFactorTestClient.PasswordStepAsync(client, email, password);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var (second, complete) = await TwoFactorTestClient.SecondFactorAsync(client, pendingAgain, enrolled.RecoveryCodes[0]);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var tokensAgain = await TokensAsync(client, complete!, watched);

        // A renovação.
        var renewed = await IdentityIssuerUserFlow.ReadTokensAsync(await IdentityIssuerUserFlow.RefreshAsync(client, tokensAgain.RefreshToken));
        WatchTokens(watched, renewed);

        // O logout.
        var logout = await client.GetAsync(QueryHelpers.AddQueryString("/connect/logout", new Dictionary<string, string?>
        {
            [Parameters.IdTokenHint] = renewed.IdToken,
            [Parameters.PostLogoutRedirectUri] = IdentityIssuerUserFlow.PostLogoutRedirectUri,
        }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        _ = tokens;

        AssertNoLeak(before, watched);
    }

    [Fact]
    public async Task GoogleLogin_LogsNoPersonalDataNorSecret()
    {
        var watched = new Watched();
        var person = await SignedInUser.CreateAsync(fixture, "log-google", name: $"Pessoa Google {Guid.NewGuid():N}"[..26]);
        var account = GoogleFlow.NewAccount(LocalUserSeed.UniqueEmail("log-google-conta"));
        await GoogleFlow.LinkAsync(fixture.Services, person.User.Id, account);
        WatchAccount(watched, account);
        watched.Add("e-mail", person.User.Email);
        watched.Add("nome", person.User.Name);
        var before = fixture.Logs.Lines.Count;

        var client = GoogleFlow.Client(fixture);
        var started = await GoogleFlow.StartAsync(client);
        var code = GoogleFlow.Consent(fixture, started, account);
        watched.Add("código do Google", code);
        var returned = await GoogleFlow.ReturnAsync(client, code, started.Query["state"], started.FlowCookie);
        Assert.Equal("totp", returned.Query["segundoPasso"]);
        var recovery = TwoFactorTestClient.NextRecoveryCode(person.User.Email);
        watched.Add("código de recuperação", recovery);
        var (second, complete) = await TwoFactorTestClient.SecondFactorAsync(client, returned.Cookie("buteco-sessao"), recovery);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        await TokensAsync(client, complete!, watched);

        AssertNoLeak(before, watched);
    }

    [Fact]
    public async Task GoogleInvitationAccept_LogsNoPersonalDataNorSecret()
    {
        var watched = new Watched();
        var admin = await SignedInUser.CreateAsync(fixture, "log-aceite-admin");
        var client = GoogleFlow.Client(fixture);
        var email = LocalUserSeed.UniqueEmail("log-aceite");
        var name = $"Pessoa Convidada {Guid.NewGuid():N}"[..30];
        watched.Add("e-mail", email);
        watched.Add("nome", name);
        watched.Add("senha", admin.Password);
        var before = fixture.Logs.Lines.Count;

        var invite = await AccessLinkClient.InviteAsync(client, admin, name, email, admin.Password, papel: "operador");
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        var token = await AccessLinkClient.TokenAsync(invite);
        watched.Add("token do convite", token);
        var account = GoogleFlow.NewAccount(email);
        WatchAccount(watched, account);
        var started = await GoogleFlow.StartInvitationAsync(client, token);
        var code = GoogleFlow.Consent(fixture, started, account);
        watched.Add("código do Google", code);
        var returned = await GoogleFlow.ReturnAsync(client, code, started.Query["state"], started.FlowCookie);
        Assert.Equal("aceito", returned.Query["convite"]);
        var enrolled = await TwoFactorTestClient.EnrollAsync(client, fixture.Services, returned.Cookie("buteco-sessao")!);
        watched.Add("segredo das 2 etapas", Base32.Encode(enrolled.Secret));
        await TokensAsync(client, enrolled.SessionCookie, watched);

        AssertNoLeak(before, watched);
    }

    [Fact]
    public async Task Connect_LogsNoPersonalDataNorSecret()
    {
        var watched = new Watched();
        var person = await SignedInUser.CreateAsync(fixture, "log-conectar");
        var account = GoogleFlow.NewAccount(LocalUserSeed.UniqueEmail("log-conectar-conta"));
        WatchAccount(watched, account);
        watched.Add("senha", person.Password);
        watched.Add("e-mail", person.User.Email);
        var before = fixture.Logs.Lines.Count;

        var client = GoogleFlow.Client(fixture);
        var connect = new HttpRequestMessage(HttpMethod.Post, "/account/federated/corporativo/connect") { Content = JsonContent.Create(new { senha = person.Password }) };
        connect.Headers.Authorization = new AuthenticationHeaderValue("Bearer", person.AccessToken);
        var started = GoogleFlow.Read(await client.SendAsync(connect));
        var code = GoogleFlow.Consent(fixture, started, account);
        watched.Add("código do Google", code);
        var returned = await GoogleFlow.ReturnAsync(client, code, started.Query["state"], started.FlowCookie);
        Assert.Equal("/account?conectado=corporativo", returned.Location);

        AssertNoLeak(before, watched);
    }

    [Fact]
    public async Task ServiceAndApplicationClientCredentials_LogNoSecret()
    {
        var watched = new Watched();
        await ServiceClientTokens.EnsureAsync(fixture.Services);
        var admin = await SignedInUser.CreateAsync(fixture, "log-aplicacao");
        watched.Add("senha", admin.Password);
        watched.Add("client_secret", ServiceClientTokens.SecretOf(IdentityClients.InboxServiceClientId));
        watched.Add("client_secret", FakeGoogle.ClientSecret);
        var before = fixture.Logs.Lines.Count;

        var service = await ServiceClientTokens.AccessTokenAsync(fixture, IdentityClients.InboxServiceClientId);
        watched.Add("token", service);

        var created = await TwoFactorTestClient.SendAsync(GoogleFlow.Client(fixture), HttpMethod.Post, "/applications/", admin.AccessToken,
            new { nome = $"Aplicação {Guid.NewGuid():N}"[..30], senha = admin.Password });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var clientId = body.GetProperty("clientId").GetString()!;
        var secret = body.GetProperty("segredo").GetProperty("valor").GetString()!;
        watched.Add("client_secret", secret);
        var response = await SignedInUser.Anonymous(fixture).PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [Parameters.GrantType] = GrantTypes.ClientCredentials,
            [Parameters.ClientId] = clientId,
            [Parameters.ClientSecret] = secret,
            [Parameters.Scope] = IdentityClients.ApiScope,
        }));
        var tokenBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, tokenBody);
        watched.Add("token", JsonDocument.Parse(tokenBody).RootElement.GetProperty(Parameters.AccessToken).GetString());

        AssertNoLeak(before, watched);
    }

    // ---- Apoio ----

    private async Task<IdentityIssuerUserFlow.Tokens> TokensAsync(HttpClient client, string sessionCookie, Watched watched)
    {
        await IdentityIssuerUserFlow.EnsurePanelClientAsync(fixture.Services);
        var pkce = IdentityIssuerUserFlow.Pkce.Create();
        watched.Add("code_verifier", pkce.Verifier);
        var authorize = await IdentityIssuerUserFlow.AuthorizeAsync(client, IdentityIssuerUserFlow.AuthorizeUrl(pkce), sessionCookie);
        var code = IdentityIssuerUserFlow.CodeFrom(authorize);
        watched.Add("código do authorize", code);
        var tokens = await IdentityIssuerUserFlow.ReadTokensAsync(await IdentityIssuerUserFlow.RedeemAsync(client, code, pkce.Verifier));
        WatchTokens(watched, tokens);
        return tokens;
    }

    private static void WatchTokens(Watched watched, IdentityIssuerUserFlow.Tokens tokens)
    {
        watched.Add("access token", tokens.AccessToken);
        watched.Add("refresh token", tokens.RefreshToken);
        watched.Add("id token", tokens.IdToken);
    }

    private static void WatchAccount(Watched watched, FakeGoogle.Account account)
    {
        watched.Add("sub do Google", account.Subject);
        watched.Add("e-mail do Google", account.Email);
    }

    private void AssertNoLeak(int before, Watched watched)
    {
        var leaks = fixture.Logs.Lines.Skip(before)
            .SelectMany(line => watched.Values
                .Where(value => line.Message.Contains(value.Value, StringComparison.OrdinalIgnoreCase))
                .Select(value => $"{line.Category}[{line.EventId.Id}] {line.Level}: {value.Kind}"))
            .Distinct()
            .ToList();
        Assert.True(leaks.Count == 0, "linhas de log com dado pessoal ou segredo:\n" + string.Join("\n", leaks));
    }
}

/// <summary>O host real, com os níveis do <c>appsettings.json</c>: o guarda.</summary>
public sealed class LogPrivacyFixture : FederatedLoginFixture;

public class LogPrivacyTests(LogPrivacyFixture fixture) : LogPrivacyFlows<LogPrivacyFixture>(fixture);
