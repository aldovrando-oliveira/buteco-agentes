extern alias ApiAssembly;

using System.Net;
using System.Net.Http.Headers;
using InboxOrchestratorRoundTrip.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace InboxOrchestratorRoundTrip.Tests;

/// <summary>
/// Acordo com o emissor real (spec <c>issuer-token-validation</c>; design.md das changes
/// validacao-jwks-inbox-connectors, D9, e usuarios-locais-login-emissor, D5; convenção 11):
/// o token de usuário do fluxo real (sessão, authorize com PKCE e troca do código, pelo
/// cliente público <c>buteco-painel</c> que o próprio <c>apps/api</c> mantém) é aceito pelo
/// <c>apps/inbox</c>, com as chaves obtidas do <c>apps/api</c> pelo endereço interno, e
/// continua aceito com o <c>apps/api</c> inalcançável. Prova com o artefato real o que os
/// testes do <c>apps/inbox</c> provam contra o <c>TestIssuer</c>: <c>typ</c>, <c>kid</c>,
/// <c>iss</c> com barra final, <c>client_id</c> e <c>aud</c>.
/// <para>
/// Até a #180 o teste cadastrava um <c>buteco-painel</c> confidencial; a sincronização do
/// catálogo o sobrescreveria para público, e ele deixou de existir.
/// </para>
/// </summary>
public class IssuerTokenRoundTripTests(RoundTripFixture fixture) : IClassFixture<RoundTripFixture>
{
    [Fact]
    public async Task RealUserToken_IsAcceptedByTheInbox_AndStillAcceptedWithTheApiUnreachable()
    {
        var accessToken = await IssuerUserFlow.AccessTokenAsync(
            fixture.ApiFactory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false }),
            WarmUpAsync,
            RoundTripFixture.ApiPublicBaseUrl,
            RoundTripFixture.OperatorPassword,
            "openid offline_access buteco-inbox");

        var token = new JsonWebToken(accessToken);
        Assert.Equal("at+jwt", token.Typ);
        Assert.False(string.IsNullOrEmpty(token.Kid));
        Assert.Equal(RoundTripFixture.ApiPublicBaseUrl + "/", token.Issuer);
        Assert.Equal("buteco-painel", token.GetPayloadValue<string>("client_id"));
        Assert.Equal(["buteco-inbox"], token.Audiences);
        Assert.True(Guid.TryParse(token.Subject, out _), $"sub não é o id do usuário: {token.Subject}");

        var inbox = fixture.InboxFactory.CreateClient();
        inbox.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // A primeira busca em segundo plano pode ter acontecido antes de o anel de chaves
        // do apps/api existir; o 503 é o "ainda sem chave", e a próxima tentativa resolve.
        var first = await PollAsync(() => inbox.GetAsync("/channels"), response => response.StatusCode != HttpStatusCode.ServiceUnavailable);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("/.well-known/openid-configuration", fixture.ApiIssuerKeys.PassedPaths);
        Assert.Contains("/.well-known/jwks", fixture.ApiIssuerKeys.PassedPaths);

        fixture.ApiIssuerKeys.Closed = true;
        try
        {
            var second = await inbox.GetAsync("/channels");
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        }
        finally
        {
            fixture.ApiIssuerKeys.Closed = false;
        }
    }

    private async Task WarmUpAsync()
    {
        await using var scope = fixture.ApiFactory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApiAssembly::Buteco.Api.Identity.Clients.IIdentityClientCatalog>().SyncAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<ApiAssembly::Buteco.Api.Users.Bootstrap.IUserBootstrapper>().RunAsync(CancellationToken.None);
    }

    private static async Task<HttpResponseMessage> PollAsync(Func<Task<HttpResponseMessage>> send, Func<HttpResponseMessage, bool> done)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            var response = await send();
            if (done(response) || DateTime.UtcNow > deadline)
            {
                return response;
            }

            await Task.Delay(200);
        }
    }
}
