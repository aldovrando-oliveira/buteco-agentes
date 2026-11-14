using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Client;

namespace Buteco.Api.Tests.Identity.Federated;

/// <summary>
/// O provedor que sai da configuração entre o início e o retorno (spec <c>federated-login</c>, "Decisão do login
/// no retorno" e "As recusas do aceite não criam sessão e voltam ao convite"; design.md da change
/// aceite-convite-google, D3 e D9; issue #276). Na M3 há um provedor por issuer (regra 6 do boot), e as duas
/// formas são: a chave trocada (a registração do state token não existe mais) e nenhum provedor configurado.
///
/// A chave trocada é montada tirando a registração das opções do cliente OIDC depois do início, que é o que o
/// host reiniciado sem ela teria. A registração tirada não volta: um host por intenção. Sem o
/// <c>FederatedMissingRegistrationHandler</c>, os três casos recebiam <c>500</c> (guarda, tasks.md 4.10).
/// </summary>
public abstract class RenamedProviderFixture : FederatedLoginFixture
{
    /// <summary>Tira a registração da chave <see cref="FederatedLoginFixture.Key"/> das opções do cliente OIDC.</summary>
    public void RemoveTheRegistration() =>
        Assert.Equal(1, Services.GetRequiredService<IOptionsMonitor<OpenIddictClientOptions>>().CurrentValue.Registrations
            .RemoveAll(registration => registration.RegistrationId == Key));
}

public sealed class RenamedProviderLoginFixture : RenamedProviderFixture;

public sealed class RenamedProviderConnectFixture : RenamedProviderFixture;

public sealed class RenamedProviderInvitationFixture : RenamedProviderFixture;

public class FederatedRenamedProviderLoginTests(RenamedProviderLoginFixture fixture) : IClassFixture<RenamedProviderLoginFixture>
{
    [Fact]
    public async Task TheLogin_WithTheKeyRenamedInTheMiddle_IsFailure_WithoutSession()
    {
        var user = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("login-renomeado"), "senha-de-teste-123");
        var account = GoogleFlow.NewAccount(user.Email);
        await GoogleFlow.LinkAsync(fixture.Services, user.Id, account);
        var client = GoogleFlow.Client(fixture);
        var started = await GoogleFlow.StartAsync(client);
        var code = GoogleFlow.Consent(fixture, started, account);
        fixture.RemoveTheRegistration();
        var before = fixture.Logs.Lines.Count;

        var returned = await GoogleFlow.ReturnAsync(client, code, started.Query["state"], started.FlowCookie);

        Assert.Equal(HttpStatusCode.Redirect, returned.Response.StatusCode);
        Assert.Equal("/entrar?recusa=falha&provedor=corporativo", returned.Location);
        Assert.Null(returned.Cookie("buteco-sessao"));
        RemovedProviderAssertions.FailureLogged(fixture, before);
    }
}

public class FederatedRenamedProviderConnectTests(RenamedProviderConnectFixture fixture) : IClassFixture<RenamedProviderConnectFixture>
{
    [Fact]
    public async Task TheConnect_WithTheKeyRenamedInTheMiddle_IsFailure_WithoutLink()
    {
        var person = await SignedInUser.CreateAsync(fixture, "conectar-renomeado");
        var client = GoogleFlow.Client(fixture);
        var connect = new HttpRequestMessage(HttpMethod.Post, "/account/federated/corporativo/connect") { Content = JsonContent.Create(new { senha = person.Password }) };
        connect.Headers.Authorization = new AuthenticationHeaderValue("Bearer", person.AccessToken);
        var started = GoogleFlow.Read(await client.SendAsync(connect));
        Assert.Equal(HttpStatusCode.OK, started.Response.StatusCode);
        var code = GoogleFlow.Consent(fixture, started, GoogleFlow.NewAccount(LocalUserSeed.UniqueEmail("conta-do-conectar")));
        fixture.RemoveTheRegistration();
        var before = fixture.Logs.Lines.Count;

        var returned = await GoogleFlow.ReturnAsync(client, code, started.Query["state"], started.FlowCookie);

        Assert.Equal(HttpStatusCode.Redirect, returned.Response.StatusCode);
        Assert.Equal("/account?recusa=falha&provedor=corporativo", returned.Location);
        Assert.Null(returned.Cookie("buteco-sessao"));
        Assert.Empty(await RemovedProviderAssertions.LinksOfAsync(fixture, person.User.Id));
        RemovedProviderAssertions.FailureLogged(fixture, before);
    }
}

public class FederatedRenamedProviderInvitationTests(RenamedProviderInvitationFixture fixture) : IClassFixture<RenamedProviderInvitationFixture>
{
    [Fact]
    public async Task TheAccept_WithTheKeyRenamedInTheMiddle_IsFailure_BackToTheInvitation_StillPending()
    {
        var caller = await SignedInUser.CreateAsync(fixture, "aceite-renomeado");
        var invited = await InvitationSeed.InvitedAsync(fixture.Services, caller.User.Id);
        var client = GoogleFlow.Client(fixture);
        var started = await GoogleFlow.StartInvitationAsync(client, invited.Token);
        Assert.Equal(HttpStatusCode.OK, started.Response.StatusCode);
        var code = GoogleFlow.Consent(fixture, started, GoogleFlow.NewAccount(invited.User.Email));
        fixture.RemoveTheRegistration();
        var before = fixture.Logs.Lines.Count;

        var returned = await GoogleFlow.ReturnAsync(client, code, started.Query["state"], started.FlowCookie);

        // Medido no apply (tasks.md 4.9): as propriedades do state chegam à rota depois da rejeição, e a falha
        // volta ao convite.
        Assert.Equal(HttpStatusCode.Redirect, returned.Response.StatusCode);
        Assert.Equal("/convite?recusa=falha&provedor=corporativo", returned.Location);
        Assert.Null(returned.Cookie("buteco-sessao"));
        Assert.Empty(await RemovedProviderAssertions.LinksOfAsync(fixture, invited.User.Id));
        var reloaded = await SignedInUser.ReloadAsync(fixture.Services, invited.User.Id);
        Assert.Equal(UserStatus.Invited, reloaded.Status);
        Assert.Equal(invited.User.AccessLinkHash, reloaded.AccessLinkHash);
        RemovedProviderAssertions.FailureLogged(fixture, before);
    }
}

/// <summary>A outra forma: o host reiniciado sem nenhum provedor (o único tirado). O state nem é lido.</summary>
public sealed class NoProviderFixture : LocalUsersFixture;

public class FederatedNoProviderTests(NoProviderFixture fixture) : IClassFixture<NoProviderFixture>
{
    [Fact]
    public async Task TheReturn_WithNoProviderConfigured_IsFailure_ToTheLogin_WithoutSession()
    {
        var response = await SignedInUser.Anonymous(fixture).GetAsync("/auth/federated/callback?code=codigo&state=state-de-um-fluxo-anterior");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/entrar?recusa=falha", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain(GoogleFlow.SetCookiesOf(response), cookie => cookie.StartsWith("buteco-sessao=", StringComparison.Ordinal));
    }
}

internal static class RemovedProviderAssertions
{
    public static void FailureLogged(FederatedLoginFixture fixture, int before) =>
        Assert.Contains(fixture.Logs.Lines.Skip(before), line => line.EventId.Id == 1199 && line.Message.Contains("invalid_request", StringComparison.Ordinal));

    public static async Task<List<Buteco.Api.Users.Federated.UserFederatedIdentity>> LinksOfAsync(FederatedLoginFixture fixture, Guid userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserFederatedIdentities.AsNoTracking().Where(link => link.UserId == userId).ToListAsync();
    }
}
