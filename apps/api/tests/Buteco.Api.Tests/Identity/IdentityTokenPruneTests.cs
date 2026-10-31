using System.Net;
using System.Text.Json;
using Buteco.Api.Identity.Storage;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using OpenIddict.Server;
using Xunit.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// A limpeza contra o que o OpenIddict real grava (spec <c>issuer-token-storage</c>;
/// design.md da change limpeza-tokens-openiddict, D3 e D7): remove o que está morto há
/// mais de 14 dias, preserva o que ainda vale e o que a revogação precisa.
///
/// <para>
/// O servidor de teste liga o grant de refresh, e cada sessão é um cliente de fixture com
/// <c>gt:refresh_token</c> (<see cref="IdentityIssuerTestClient.EnsureRefreshClientAsync"/>):
/// o <c>client_credentials</c> com <c>offline_access</c> grava autorização ad hoc e refresh
/// token, e cada renovação grava um refresh novo. Desde a #180 (D3.8), nenhum access token
/// tem linha, nem o da renovação. O relógio
/// do app começa 30 dias atrás e anda até o presente; o OpenIddict grava as datas por ele
/// (F15). O "vencido" do critério é o relógio do banco (D3), daí as margens de dias.
/// </para>
/// <para>
/// Um teste só, em passos, porque a limpeza é global e o relógio só anda para a frente.
/// </para>
/// <para>
/// Verificado contra o defeito (convenção 15): com <c>MinimumAge</c> trocado por zero, o
/// refresh resgatado há 1 dia sai, e o reuso deixa de revogar a cadeia (registro no
/// tasks.md, 3.5).
/// </para>
/// </summary>
public class IdentityTokenPruneTests(IdentityTokenPruneTests.PruneFixture factory, ITestOutputHelper output)
    : IClassFixture<IdentityTokenPruneTests.PruneFixture>
{
    public sealed class PruneFixture : ApiFactoryFixture
    {
        /// <summary>O presente do teste; o relógio do app começa 30 dias antes.</summary>
        public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;

        public FakeTimeProvider Clock { get; }

        public PruneFixture()
        {
            Clock = new FakeTimeProvider(Now - TimeSpan.FromDays(30));

            // A checagem de fuso do boot compara TZ com o fuso deste relógio.
            Clock.SetLocalTimeZone(TimeZoneInfo.Local);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);

                // O relógio pula dias: sem isto, o serviço rodaria a limpeza sozinho no
                // meio dos passos. O teste chama o RunCycleAsync direto.
                services.RemoveAll<IdentityTokenPruneSchedule>();
                services.AddSingleton(new IdentityTokenPruneSchedule { Interval = Timeout.InfiniteTimeSpan });

                // Só no teste: em produção o refresh chega com a #180.
                services.AddOpenIddict().AddServer(server => server.AllowRefreshTokenFlow());
            });
        }
    }

    private const string DeadSession = "sessao-antiga-morta";
    private const string OldLiveSession = "sessao-antiga-viva";
    private const string RevokedSession = "sessao-revogada";
    private const string ReusedSession = "sessao-reuso";
    private const string LiveSession = "sessao-viva";
    private const string RowlessSession = "sessao-sem-linha";

    [Fact]
    public async Task Prune_RemovesWhatIsDeadAndOld_KeepsWhatStillCounts_AndRevocationStillHolds()
    {
        foreach (var clientId in new[] { DeadSession, OldLiveSession, RevokedSession, ReusedSession, LiveSession, RowlessSession })
        {
            await IdentityIssuerTestClient.EnsureRefreshClientAsync(factory.Services, clientId);
        }

        var now = factory.Now;

        // 30 dias atrás: uma sessão que renova uma vez e para. Tudo dela morre.
        var dead = await SessionAsync(DeadSession);
        At(now - TimeSpan.FromDays(30) + TimeSpan.FromMinutes(2));
        await RefreshOkAsync(DeadSession, dead);

        // 20 dias atrás: uma sessão que continua renovando até ontem.
        At(now - TimeSpan.FromDays(20));
        var oldLive = await SessionAsync(OldLiveSession);

        // 16 dias atrás: uma sessão revogada no dia seguinte.
        At(now - TimeSpan.FromDays(16));
        var revoked = await SessionAsync(RevokedSession);
        At(now - TimeSpan.FromDays(15));
        await RevokeAuthorizationsAsync(RevokedSession);

        At(now - TimeSpan.FromDays(10));
        oldLive = await RefreshOkAsync(OldLiveSession, oldLive);

        // Ontem: a sessão antiga renova de novo, e uma sessão nova resgata o primeiro refresh.
        At(now - TimeSpan.FromDays(1));
        oldLive = await RefreshOkAsync(OldLiveSession, oldLive);
        var reusedFirst = await SessionAsync(ReusedSession);
        At(now - TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));
        await RefreshOkAsync(ReusedSession, reusedFirst);

        // Agora.
        At(now);
        var live = await SessionAsync(LiveSession);

        // Removíveis: DeadSession (refresh inicial resgatado e o refresh da renovação), o
        // primeiro refresh de OldLiveSession (resgatado há 20 dias) e o de RevokedSession; e
        // as autorizações de DeadSession e de RevokedSession, que ficam sem token. Nenhum
        // access token tem linha (D1 da #209, estendido pela #180 D3.8): eram 5 tokens
        // antes da #180, com o access da renovação de DeadSession.
        var pruner = factory.Services.GetRequiredService<IIdentityTokenPruner>();
        IdentityTokenPruneResult result = null!;
        var commands = await factory.SqlCapture.CaptureAsync(async () => result = await pruner.RunCycleAsync(CancellationToken.None));

        Assert.Equal(IdentityTokenPruneResult.Pruned(tokens: 4, authorizations: 2), result);

        // O "vencido" dos tokens é o relógio do Postgres, dentro do DELETE (D3).
        var tokenDeletes = commands.Where(command => command.Contains("DELETE FROM openiddict_tokens", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(tokenDeletes);
        foreach (var command in commands.Where(command => command.Contains("DELETE FROM", StringComparison.Ordinal)))
        {
            output.WriteLine(command);
            output.WriteLine("---");
        }

        Assert.All(tokenDeletes, command => Assert.Contains("\"ExpirationDate\" < now()", command, StringComparison.Ordinal));

        // Remove o morto antigo.
        Assert.Empty(await TokensAsync(DeadSession));
        Assert.Empty(await AuthorizationsAsync(DeadSession));
        Assert.Empty(await TokensAsync(RevokedSession));
        Assert.Empty(await AuthorizationsAsync(RevokedSession));

        // Preserva o morto recente e a sessão antiga ainda válida.
        var oldLiveTokens = await TokensAsync(OldLiveSession);
        Assert.DoesNotContain(oldLiveTokens, token => token.CreationDate < now - TimeSpan.FromDays(14));
        Assert.Contains(oldLiveTokens, token => token.Type == TokenTypeIdentifiers.RefreshToken && token.Status == Statuses.Redeemed);
        Assert.Contains(oldLiveTokens, token => token.Type == TokenTypeIdentifiers.RefreshToken && token.Status == Statuses.Valid);
        Assert.Single(await AuthorizationsAsync(OldLiveSession));
        Assert.Contains(await TokensAsync(ReusedSession), token => token.Status == Statuses.Redeemed);
        Assert.Contains(await TokensAsync(LiveSession), token => token.Status == Statuses.Valid);

        At(now + TimeSpan.FromMinutes(1));

        // A sessão antiga válida continua renovando depois da limpeza.
        await RefreshOkAsync(OldLiveSession, oldLive);

        // A sessão viva continua revogável depois da limpeza.
        await RevokeAuthorizationsAsync(LiveSession);
        await RefreshRejectedAsync(LiveSession, live);

        // O refresh resgatado ontem, reapresentado, revoga a cadeia: a linha resgatada ficou.
        await RefreshRejectedAsync(ReusedSession, reusedFirst);
        Assert.All(
            (await TokensAsync(ReusedSession)).Where(token => token.Type == TokenTypeIdentifiers.RefreshToken),
            token => Assert.NotEqual(Statuses.Valid, token.Status));

        // A sessão revogada e removida continua recusada.
        await RefreshRejectedAsync(RevokedSession, revoked);

        // Sem a linha, o token é recusado mesmo no prazo (F8): remover nunca aceita o que
        // a revogação recusaria.
        var rowless = await SessionAsync(RowlessSession);
        await DeleteTokensAsync(RowlessSession);
        await RefreshRejectedAsync(RowlessSession, rowless);
    }

    private void At(DateTimeOffset instant) => factory.Clock.SetUtcNow(instant);

    private HttpClient AnonymousClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }

    private async Task<string> SessionAsync(string clientId)
    {
        var response = await AnonymousClient().PostAsync("/connect/token", IdentityIssuerTestClient.RefreshSessionRequest(clientId));
        return RefreshTokenOf(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<string> RefreshOkAsync(string clientId, string refreshToken)
    {
        var response = await AnonymousClient().PostAsync("/connect/token", IdentityIssuerTestClient.RefreshRequest(clientId, refreshToken));
        return RefreshTokenOf(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task RefreshRejectedAsync(string clientId, string refreshToken)
    {
        var response = await AnonymousClient().PostAsync("/connect/token", IdentityIssuerTestClient.RefreshRequest(clientId, refreshToken));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, body);
        Assert.Equal(Errors.InvalidGrant, JsonDocument.Parse(body).RootElement.GetProperty("error").GetString());
    }

    private static string RefreshTokenOf(HttpStatusCode status, string body)
    {
        Assert.True(status == HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.GetProperty(Parameters.RefreshToken).GetString()!;
    }

    private async Task RevokeAuthorizationsAsync(string subject)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var revoked = await scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>().RevokeBySubjectAsync(subject);
        Assert.Equal(1, revoked);
    }

    private async Task DeleteTokensAsync(string subject)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var deleted = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Set<OpenIddictEntityFrameworkCoreToken>().Where(token => token.Subject == subject).ExecuteDeleteAsync();
        Assert.Equal(1, deleted);
    }

    private async Task<List<OpenIddictEntityFrameworkCoreToken>> TokensAsync(string subject)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Set<OpenIddictEntityFrameworkCoreToken>().AsNoTracking().Where(token => token.Subject == subject).ToListAsync();
    }

    private async Task<List<OpenIddictEntityFrameworkCoreAuthorization>> AuthorizationsAsync(string subject)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Set<OpenIddictEntityFrameworkCoreAuthorization>().AsNoTracking().Where(authorization => authorization.Subject == subject).ToListAsync();
    }
}
