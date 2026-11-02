using System.Net;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;

namespace Buteco.Api.Tests.Users.Management;

/// <summary>
/// "Desativar outro usuário, sem nunca ficar sem usuário ativo" (spec <c>user-management</c>;
/// design.md da change rotas-conta-usuarios, D2 e D7). O estado desativado no login e na
/// renovação é do <c>UserDeactivationTests</c> (#180); aqui é a rota.
///
/// <para>
/// A corrida segue a quinta forma da convenção 15: o comportamental (20 rodadas) pode passar
/// por sorte, e a asserção sobre o SQL emitido (lock, releitura de quem chama, <c>UPDATE</c>,
/// nessa ordem) reprova sempre que o lock ou a releitura saem.
/// </para>
///
/// <para>
/// Verificado contra o defeito, registro no tasks.md, 6.6 e 6.7:
/// <list type="bullet">
/// <item>revogação retirada → <see cref="DeactivateSomeoneElse_EndsTheirSessions"/> reprova;</item>
/// <item>recusa de si mesmo retirada → <see cref="DeactivateYourself_Is403"/> reprova;</item>
/// <item>idempotência retirada → <see cref="DeactivateAgain_Is200_AndTheStampStays"/> reprova;</item>
/// <item>lock retirado, ou a releitura de quem chama tirada de dentro dele →
/// <see cref="ADeactivation_TakesTheLock_ThenRereadsTheCaller_ThenWrites"/> reprova em toda execução.</item>
/// </list>
/// </para>
/// </summary>
public class UserDeactivationRoutesTests(UserDeactivationRoutesTests.Fixture factory, Xunit.Abstractions.ITestOutputHelper output)
    : IClassFixture<UserDeactivationRoutesTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private Task<HttpResponseMessage> DeactivateAsync(SignedInUser caller, Guid id) =>
        TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Post, $"/users/{id}/deactivate", caller.AccessToken);

    [Fact]
    public async Task DeactivateSomeoneElse_EndsTheirSessions()
    {
        var caller = await SignedInUser.CreateAsync(factory, "desativa-quem-chama");
        var target = await SignedInUser.CreateAsync(factory, "desativa-alvo");

        var response = await DeactivateAsync(caller, target.User.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await TwoFactorTestClient.JsonAsync(response);
        Assert.Equal("desativado", body.GetProperty("estado").GetString());
        Assert.Equal(0, body.GetProperty("sessoes").GetProperty("ativas").GetInt32());
        Assert.All(await UserSessionsSeed.AuthorizationStatusesAsync(factory.Services, target.User.Id), status => Assert.Equal("revoked", status));
        Assert.False(await SignedInUser.RenewsAsync(factory, target.RefreshToken));
        Assert.Equal(HttpStatusCode.Forbidden, (await LocalUserSeed.PostSessionAsync(SignedInUser.Anonymous(factory), target.User.Email, target.Password)).StatusCode);
    }

    [Fact]
    public async Task DeactivateYourself_Is403()
    {
        var caller = await SignedInUser.CreateAsync(factory, "a-si-mesmo");

        Assert.Equal(HttpStatusCode.Forbidden, (await DeactivateAsync(caller, caller.User.Id)).StatusCode);

        Assert.True((await SignedInUser.ReloadAsync(factory.Services, caller.User.Id)).IsActive);
    }

    [Fact]
    public async Task DeactivateAgain_Is200_AndTheStampStays()
    {
        var caller = await SignedInUser.CreateAsync(factory, "de-novo-quem-chama");
        var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("de-novo"), SignedInUser.DefaultPassword);
        Assert.Equal(HttpStatusCode.OK, (await DeactivateAsync(caller, target.Id)).StatusCode);
        var stamp = (await SignedInUser.ReloadAsync(factory.Services, target.Id)).SecurityStamp;

        Assert.Equal(HttpStatusCode.OK, (await DeactivateAsync(caller, target.Id)).StatusCode);

        Assert.Equal(stamp, (await SignedInUser.ReloadAsync(factory.Services, target.Id)).SecurityStamp);
    }

    [Fact]
    public async Task UnknownUser_Is404()
    {
        var caller = await SignedInUser.CreateAsync(factory, "desativa-404");

        Assert.Equal(HttpStatusCode.NotFound, (await DeactivateAsync(caller, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task TwoUsersDeactivatingEachOther_OneWins_OneIs403_AndOneStaysActive()
    {
        var reproduced = 0;
        for (var round = 0; round < 20; round++)
        {
            var a = await SignedInUser.CreateAsync(factory, "corrida-a");
            var b = await SignedInUser.CreateAsync(factory, "corrida-b");

            var responses = await Task.WhenAll(DeactivateAsync(a, b.User.Id), DeactivateAsync(b, a.User.Id));

            var statuses = responses.Select(response => response.StatusCode).Order().ToList();
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Forbidden], statuses);
            var stillActive = new[] { await SignedInUser.ReloadAsync(factory.Services, a.User.Id), await SignedInUser.ReloadAsync(factory.Services, b.User.Id) }
                .Count(user => user.IsActive);
            Assert.Equal(1, stillActive);

            // A segunda chegou ao lock com quem chama ainda ativo na primeira leitura: a corrida aconteceu.
            reproduced += (await TwoFactorTestClient.TitleAsync(responses.Single(response => response.StatusCode == HttpStatusCode.Forbidden)))
                == CurrentUser.ForbiddenMessage ? 1 : 0;
        }

        output.WriteLine($"rodadas em que a segunda desativação passou pelo CurrentUser e caiu na releitura dentro do lock: {reproduced} de 20");
    }

    [Fact]
    public async Task ADeactivation_TakesTheLock_ThenRereadsTheCaller_ThenWrites()
    {
        var caller = await SignedInUser.CreateAsync(factory, "sql-quem-chama");
        var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("sql-alvo"), SignedInUser.DefaultPassword);

        var commands = await factory.SqlCapture.CaptureAsync(async () =>
            Assert.Equal(HttpStatusCode.OK, (await DeactivateAsync(caller, target.Id)).StatusCode));

        var executed = commands.ToList();
        var lockAt = executed.FindIndex(command => command.Contains("pg_advisory_xact_lock", StringComparison.Ordinal));
        var rereadAt = executed.FindIndex(command => command.Contains("EXISTS", StringComparison.Ordinal) && command.Contains("FROM users", StringComparison.Ordinal));
        var updateAt = executed.FindIndex(command => command.Contains("UPDATE users SET", StringComparison.Ordinal));
        Assert.True(lockAt >= 0, "a desativação não pediu o pg_advisory_xact_lock:\n" + string.Join("\n---\n", executed));
        Assert.True(rereadAt > lockAt, $"a releitura de quem chama não veio depois do lock (lock {lockAt}, releitura {rereadAt})");
        Assert.True(updateAt > rereadAt, $"o UPDATE de users não veio depois da releitura (releitura {rereadAt}, update {updateAt})");
    }
}
