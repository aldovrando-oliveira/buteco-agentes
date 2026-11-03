using System.Net;
using System.Net.Http.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users.AccessLinks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users.AccessLinks;

/// <summary>
/// "Rotas públicas do link: barreiras, limite e resposta única" (spec <c>user-access-links</c>;
/// design.md da change convites-redefinicao-senha, D8, P9).
///
/// <para>
/// Verificado contra o defeito (tasks.md, 6.8), um de cada vez: uma mensagem distinta para o
/// vencido → <see cref="EveryInvalidLink_GetsTheSameStatusAndBody"/> reprova; o propósito fora da
/// busca → o mesmo caso reprova (o link de convite regravado num ativo é aceito na rota da
/// redefinição). O limite
/// global está em <see cref="AccessLinkRateLimitTests"/>.
/// </para>
/// </summary>
public class AccessLinkPublicRoutesTests(AccessLinkPublicRoutesTests.Fixture factory) : IClassFixture<AccessLinkPublicRoutesTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private static readonly string[] Routes = ["/auth/invitation", "/auth/invitation/accept", "/auth/password-reset", "/auth/password-reset/complete"];

    private HttpClient Client => SignedInUser.Anonymous(factory);

    [Fact]
    public async Task WithoutJson_Is415_AndFromAnotherOrigin_Is403()
    {
        foreach (var route in Routes)
        {
            var form = new HttpRequestMessage(HttpMethod.Post, route) { Content = new FormUrlEncodedContent([new("token", "x")]) };
            form.Headers.Add("Origin", LocalUserSeed.IssuerOrigin);
            Assert.True((await Client.SendAsync(form)).StatusCode == HttpStatusCode.UnsupportedMediaType, $"{route} sem JSON");

            var foreign = await AccessLinkClient.PublicAsync(Client, route, new { token = "x", senha = "uma-senha-longa-qualquer" }, origin: "https://outro.exemplo.test");
            Assert.True(foreign.StatusCode == HttpStatusCode.Forbidden, $"{route} de outra origem");
        }
    }

    [Fact]
    public async Task EveryInvalidLink_GetsTheSameStatusAndBody()
    {
        var caller = await SignedInUser.CreateAsync(factory, "resposta-unica");
        var clock = factory.Clock.GetUtcNow();
        var expired = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id, expiresAt: clock.AddMinutes(-1));
        var used = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await AccessLinkClient.AcceptAsync(Client, used.Token, "senha-de-quem-aceitou")).StatusCode);
        var cancelled = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await AccessLinkClient.CancelAsync(Client, caller, cancelled.User.Id)).StatusCode);
        var resetTarget = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("proposito"), SignedInUser.DefaultPassword);
        var resetToken = await InvitationSeed.PasswordResetAsync(factory.Services, resetTarget.Id);
        var expiredResetTarget = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("redefinicao-vencida"), SignedInUser.DefaultPassword);
        var expiredReset = await InvitationSeed.PasswordResetAsync(factory.Services, expiredResetTarget.Id, expiresAt: clock.AddMinutes(-1));
        var invitation = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);

        // O estado casa com o propósito por construção (o aceite apaga o link): só um link de
        // convite regravado à mão num ativo separa a conferência do propósito da do estado.
        var mismatched = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("proposito-trocado"), SignedInUser.DefaultPassword);
        var mismatchedToken = AccessLinkToken.New();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE users SET access_link_hash = {AccessLinkToken.Hash(mismatchedToken)}, access_link_purpose = 'invitation', access_link_expires_at = {clock.AddHours(1)} WHERE id = {mismatched.Id}");
        }

        var invitationTokens = new[] { "inexistente-" + Guid.NewGuid().ToString("N"), expired.Token, used.Token, cancelled.Token, resetToken };
        var resetTokens = new[] { "inexistente-" + Guid.NewGuid().ToString("N"), expiredReset, invitation.Token, mismatchedToken };
        var responses = new List<(string What, HttpResponseMessage Response)>();
        foreach (var token in invitationTokens)
        {
            responses.Add(($"consulta do convite {token[..8]}", await AccessLinkClient.LookupInvitationAsync(Client, token)));
            responses.Add(($"aceite {token[..8]}", await AccessLinkClient.AcceptAsync(Client, token, "uma-senha-dentro-da-politica")));
        }

        foreach (var token in resetTokens)
        {
            responses.Add(($"consulta da redefinição {token[..8]}", await AccessLinkClient.LookupResetAsync(Client, token)));
            responses.Add(($"conclusão {token[..8]}", await AccessLinkClient.CompleteAsync(Client, token, "uma-senha-dentro-da-politica")));
        }

        var reference = await responses[0].Response.Content.ReadAsStringAsync();
        foreach (var (what, response) in responses)
        {
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{what} → {(int)response.StatusCode}");
            Assert.Equal(reference, await response.Content.ReadAsStringAsync());
        }

        Assert.Contains("Este link não vale mais.", reference, StringComparison.Ordinal);
    }
}
