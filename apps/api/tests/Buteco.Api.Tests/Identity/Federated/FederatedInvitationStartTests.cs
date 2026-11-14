using System.Net;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Buteco.Api.Users.AccessLinks;
using Microsoft.AspNetCore.WebUtilities;

namespace Buteco.Api.Tests.Identity.Federated;

/// <summary>
/// O início do aceite de convite pelo provedor (spec <c>federated-login</c>, "Início do aceite de convite pelo
/// provedor"; <c>user-access-links</c>, "Rotas públicas do link"; design.md da change aceite-convite-google, D1).
/// Guardas verificados contra o defeito (tasks.md, 1.4 e 1.5).
/// </summary>
public class FederatedInvitationStartTests(FederatedLoginFixture fixture) : IClassFixture<FederatedLoginFixture>
{
    [Fact]
    public async Task Start_ReturnsTheGoogleUrl_AndTheFlowCookie_WithoutTheTokenNorTheEmail()
    {
        var invited = await InvitedAsync("inicio-convite");

        // Sem Authorization: a rota é uma porta de entrada anônima.
        var started = await GoogleFlow.StartInvitationAsync(GoogleFlow.Client(fixture), invited.Token);

        Assert.Equal(HttpStatusCode.OK, started.Response.StatusCode);
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", started.Url, StringComparison.Ordinal);
        Assert.NotNull(started.FlowCookie);
        Assert.DoesNotContain(invited.Token, started.Url, StringComparison.Ordinal);
        Assert.DoesNotContain(Uri.EscapeDataString(invited.User.Email), started.Url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(invited.User.Email, started.Url, StringComparison.OrdinalIgnoreCase);
        Assert.False(started.Query.ContainsKey("login_hint"));
        Assert.Equal("select_account", started.Query["prompt"]);
    }

    [Fact]
    public async Task TheStateToken_CarriesTheHash_NotTheLink()
    {
        var invited = await InvitedAsync("state-hash");
        var before = await StateTokenReader.IdsAsync(fixture.Services);

        var started = await GoogleFlow.StartInvitationAsync(GoogleFlow.Client(fixture), invited.Token);
        Assert.Equal(HttpStatusCode.OK, started.Response.StatusCode);

        var payload = await StateTokenReader.DecryptedPayloadOfNewAsync(fixture.Services, before);
        Assert.Contains("\"buteco_intencao\":\"convite\"", payload, StringComparison.Ordinal);
        Assert.Contains(invited.User.Id.ToString(), payload, StringComparison.Ordinal);
        Assert.Contains(WebEncoders.Base64UrlEncode(AccessLinkToken.Hash(invited.Token)), payload, StringComparison.Ordinal);
        Assert.DoesNotContain(invited.Token, payload, StringComparison.Ordinal);
    }

    public static TheoryData<string> InvalidInvitations => ["vencido", "cancelado", "aceito", "redefinicao"];

    [Theory]
    [MemberData(nameof(InvalidInvitations))]
    public async Task Start_WithAnInvalidInvitation_IsTheSingleInvalidLinkAnswer_WithoutCookieNorProvider(string kind)
    {
        var caller = await SignedInUser.CreateAsync(fixture, $"inicio-invalido-{kind}");
        var client = GoogleFlow.Client(fixture);
        string token;
        switch (kind)
        {
            case "vencido":
                token = (await InvitationSeed.InvitedAsync(fixture.Services, caller.User.Id, expiresAt: fixture.Clock.GetUtcNow().AddMinutes(-1))).Token;
                break;
            case "cancelado":
                var cancelled = await InvitationSeed.InvitedAsync(fixture.Services, caller.User.Id);
                Assert.Equal(HttpStatusCode.NoContent, (await AccessLinkClient.CancelAsync(client, caller, cancelled.User.Id)).StatusCode);
                token = cancelled.Token;
                break;
            case "aceito":
                var accepted = await InvitationSeed.InvitedAsync(fixture.Services, caller.User.Id);
                Assert.Equal(HttpStatusCode.NoContent, (await AccessLinkClient.AcceptAsync(client, accepted.Token, "senha-do-aceite-antes")).StatusCode);
                token = accepted.Token;
                break;
            default:
                token = await InvitationSeed.PasswordResetAsync(fixture.Services, caller.User.Id);
                break;
        }

        var tokensBefore = fixture.Google.TokenRequests;
        var discoveryBefore = fixture.Google.DiscoveryRequests;
        var started = await GoogleFlow.StartInvitationAsync(client, token);

        Assert.Equal(HttpStatusCode.NotFound, started.Response.StatusCode);
        Assert.Contains(AccessLinkPublicEndpoints.InvalidLinkMessage, await started.Response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(GoogleFlow.SetCookiesOf(started.Response));
        Assert.Null(started.Url);
        Assert.Equal(tokensBefore, fixture.Google.TokenRequests);
        Assert.Equal(discoveryBefore, fixture.Google.DiscoveryRequests);
    }

    [Fact]
    public async Task Start_AndLookup_WithTheSameExpiredToken_AnswerTheSame()
    {
        var caller = await SignedInUser.CreateAsync(fixture, "resposta-unica");
        var expired = await InvitationSeed.InvitedAsync(fixture.Services, caller.User.Id, expiresAt: fixture.Clock.GetUtcNow().AddMinutes(-1));
        var client = GoogleFlow.Client(fixture);

        var lookup = await AccessLinkClient.LookupInvitationAsync(client, expired.Token);
        var started = await GoogleFlow.StartInvitationAsync(client, expired.Token);

        Assert.Equal(lookup.StatusCode, started.Response.StatusCode);
        Assert.Equal(await lookup.Content.ReadAsStringAsync(), await started.Response.Content.ReadAsStringAsync());
        Assert.Empty(GoogleFlow.SetCookiesOf(started.Response));
    }

    [Fact]
    public async Task Start_WithAnUnknownKey_IsNotFound()
    {
        var invited = await InvitedAsync("inicio-chave");

        var started = await GoogleFlow.StartInvitationAsync(GoogleFlow.Client(fixture), invited.Token, key: "inexistente");

        Assert.Equal(HttpStatusCode.NotFound, started.Response.StatusCode);
        Assert.Contains("Provedor não configurado.", await started.Response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(GoogleFlow.SetCookiesOf(started.Response));
    }

    [Fact]
    public async Task Start_FromAnotherOrigin_IsRejected_WithoutCookie_AndTheInvitationUnchanged()
    {
        var invited = await InvitedAsync("inicio-origem");

        var started = await GoogleFlow.StartInvitationAsync(GoogleFlow.Client(fixture), invited.Token, origin: "https://outro-site.exemplo");

        Assert.Equal(HttpStatusCode.Forbidden, started.Response.StatusCode);
        Assert.Null(started.Url);
        Assert.Empty(GoogleFlow.SetCookiesOf(started.Response));
        var reloaded = await SignedInUser.ReloadAsync(fixture.Services, invited.User.Id);
        Assert.Equal(UserStatus.Invited, reloaded.Status);
        Assert.Equal(invited.User.AccessLinkHash, reloaded.AccessLinkHash);
    }

    private async Task<InvitationSeed.Seeded> InvitedAsync(string prefix)
    {
        var caller = await SignedInUser.CreateAsync(fixture, prefix);
        return await InvitationSeed.InvitedAsync(fixture.Services, caller.User.Id);
    }
}
