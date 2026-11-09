using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Buteco.Api.Users.Management;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users.Management;

/// <summary>
/// "Mudar o papel de um usuário" e "Desativar outro usuário, sem nunca ficar sem usuário ativo"
/// (spec <c>user-management</c>; design.md da change papeis-no-token, D6): o lock <c>BUTECOD1</c>, a
/// releitura de quem chama Admin ativo dentro dele, a contagem do último Admin, a senha na promoção.
///
/// <para>
/// O banco da fixture é de todos os casos da classe. O caso do último Admin deixa só o próprio
/// usuário como Admin ativo antes (<see cref="OnlyAdminAsync"/>), e as corridas dependem só dos dois
/// usuários delas: a segunda escrita cai na releitura de quem chama, e não na contagem.
/// </para>
/// </summary>
public class UserRoleChangeTests(UserRoleChangeTests.Fixture factory) : IClassFixture<UserRoleChangeTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private Task<HttpResponseMessage> ChangeAsync(SignedInUser caller, Guid id, string papel, string? senha = null) =>
        TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Put, $"/users/{id}/role", caller.AccessToken, new { papel, senha });

    private Task<HttpResponseMessage> DeactivateAsync(SignedInUser caller, Guid id) =>
        TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Post, $"/users/{id}/deactivate", caller.AccessToken);

    private async Task<UserRole> RoleOfAsync(Guid id) => (await SignedInUser.ReloadAsync(factory.Services, id)).Role;

    // Todo outro usuário da fixture vira Leitor: o arranjo do "único Admin ativo" (ou dos dois).
    private async Task OnlyAdminAsync(Guid id, Guid? alsoAdmin = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var other = alsoAdmin ?? id;
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE users SET role = 'reader' WHERE id <> {id} AND id <> {other}");
    }

    [Fact]
    public async Task DemotingAnotherAdmin_Is200_KeepsTheStamp_AndTheirSessionRenewsWithTheNewRole()
    {
        var caller = await SignedInUser.CreateAsync(factory, "papel-quem");
        var other = await SignedInUser.CreateAsync(factory, "papel-outro");
        var stampBefore = (await SignedInUser.ReloadAsync(factory.Services, other.User.Id)).SecurityStamp;

        var response = await ChangeAsync(caller, other.User.Id, "operador");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("operador", (await TwoFactorTestClient.JsonAsync(response)).GetProperty("papel").GetString());
        Assert.Equal(stampBefore, (await SignedInUser.ReloadAsync(factory.Services, other.User.Id)).SecurityStamp);
        var renewed = await IdentityIssuerUserFlow.ReadTokensAsync(await IdentityIssuerUserFlow.RefreshAsync(SignedInUser.Anonymous(factory), other.RefreshToken));
        Assert.Contains("\"role\":\"operator\"", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(Pad(renewed.AccessToken.Split('.')[1]))), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DemotingYourself_WithAnotherAdmin_Is200_AndUsersIsForbiddenOnTheNextRequest()
    {
        var me = await SignedInUser.CreateAsync(factory, "papel-eu");
        await SignedInUser.CreateAsync(factory, "papel-outro-admin");

        var response = await ChangeAsync(me, me.User.Id, "leitor");
        var users = await TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Get, "/users", me.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, users.StatusCode);
    }

    [Fact]
    public async Task TheLastAdmin_CannotDemoteThemselves()
    {
        var me = await SignedInUser.CreateAsync(factory, "papel-ultimo");
        await OnlyAdminAsync(me.User.Id);

        var response = await ChangeAsync(me, me.User.Id, "operador");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserRoleEndpoint.LastAdminMessage, await TwoFactorTestClient.TitleAsync(response));
        Assert.Equal(UserRole.Admin, await RoleOfAsync(me.User.Id));
    }

    /// <summary>
    /// A corrida que só a contagem DENTRO do lock pega: os dois últimos Admins rebaixando a si mesmos.
    /// Cada um, antes do lock, vê dois Admins; dentro dele, o segundo vê um só e recebe <c>409</c>.
    /// </summary>
    [Fact]
    public async Task TheTwoLastAdmins_DemotingThemselvesAtOnce_LeaveOneAdmin()
    {
        for (var round = 0; round < 10; round++)
        {
            var a = await SignedInUser.CreateAsync(factory, "papel-ultimos-a");
            var b = await SignedInUser.CreateAsync(factory, "papel-ultimos-b");
            await OnlyAdminAsync(a.User.Id, b.User.Id);

            var responses = await Task.WhenAll(ChangeAsync(a, a.User.Id, "leitor"), ChangeAsync(b, b.User.Id, "leitor"));

            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(response => response.StatusCode).Order().ToList());
            var admins = new[] { await RoleOfAsync(a.User.Id), await RoleOfAsync(b.User.Id) }.Count(role => role == UserRole.Admin);
            Assert.Equal(1, admins);
        }
    }

    [Fact]
    public async Task TwoAdminsDemotingEachOther_OneWins_OneIs403_AndOneStaysAdmin()
    {
        for (var round = 0; round < 10; round++)
        {
            var a = await SignedInUser.CreateAsync(factory, "papel-corrida-a");
            var b = await SignedInUser.CreateAsync(factory, "papel-corrida-b");

            var responses = await Task.WhenAll(ChangeAsync(a, b.User.Id, "operador"), ChangeAsync(b, a.User.Id, "operador"));

            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Forbidden], responses.Select(response => response.StatusCode).Order().ToList());
            var admins = new[] { await RoleOfAsync(a.User.Id), await RoleOfAsync(b.User.Id) }.Count(role => role == UserRole.Admin);
            Assert.Equal(1, admins);
        }
    }

    [Fact]
    public async Task AChange_TakesTheLock_ThenRereadsTheCaller_ThenWrites()
    {
        var caller = await SignedInUser.CreateAsync(factory, "papel-sql");
        var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("papel-sql-alvo"), SignedInUser.DefaultPassword);

        var commands = await factory.SqlCapture.CaptureAsync(async () =>
            Assert.Equal(HttpStatusCode.OK, (await ChangeAsync(caller, target.Id, "leitor")).StatusCode));

        var executed = commands.ToList();
        var lockAt = executed.FindIndex(command => command.Contains("pg_advisory_xact_lock", StringComparison.Ordinal));
        var rereadAt = executed.FindIndex(lockAt + 1, command => command.Contains("EXISTS", StringComparison.Ordinal) && command.Contains("FROM users", StringComparison.Ordinal));
        var countAt = executed.FindIndex(command => command.Contains("count(*)", StringComparison.OrdinalIgnoreCase) && command.Contains("FROM users", StringComparison.Ordinal));
        var updateAt = executed.FindIndex(command => command.Contains("UPDATE users SET", StringComparison.Ordinal));
        Assert.True(countAt > lockAt, $"a contagem dos Admins não veio depois do lock (lock {lockAt}, contagem {countAt})");
        Assert.True(lockAt >= 0, "a mudança de papel não pediu o pg_advisory_xact_lock:\n" + string.Join("\n---\n", executed));
        Assert.True(rereadAt > lockAt, $"a releitura de quem chama não veio depois do lock (lock {lockAt}, releitura {rereadAt})");
        Assert.True(updateAt > rereadAt, $"o UPDATE de users não veio depois da releitura (releitura {rereadAt}, update {updateAt})");
    }

    [Fact]
    public async Task PromotingToAdmin_AsksForTheCallersPassword()
    {
        var caller = await SignedInUser.CreateAsync(factory, "papel-promove");
        var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("papel-promovido"), SignedInUser.DefaultPassword, role: UserRole.Operator);

        var withoutPassword = await ChangeAsync(caller, target.Id, "admin");
        var stillOperator = await RoleOfAsync(target.Id);
        var failures = await FailuresOfAsync(caller.User.NormalizedEmail);
        var withPassword = await ChangeAsync(caller, target.Id, "admin", caller.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, withoutPassword.StatusCode);
        Assert.Equal(UserRole.Operator, stillOperator);
        Assert.Equal(1, failures);
        Assert.Equal(HttpStatusCode.OK, withPassword.StatusCode);
        Assert.Equal("admin", (await TwoFactorTestClient.JsonAsync(withPassword)).GetProperty("papel").GetString());
    }

    [Fact]
    public async Task AnUnknownRole_Is400_AndWritesNothing()
    {
        var caller = await SignedInUser.CreateAsync(factory, "papel-invalido");
        var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("papel-invalido-alvo"), SignedInUser.DefaultPassword);

        var response = await ChangeAsync(caller, target.Id, "superusuario");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(UserRole.Admin, await RoleOfAsync(target.Id));
    }

    [Fact]
    public async Task TwoAdminsDeactivatingAndDemotingEachOther_LeaveOneActiveAdmin()
    {
        for (var round = 0; round < 10; round++)
        {
            var a = await SignedInUser.CreateAsync(factory, "papel-cruza-a");
            var b = await SignedInUser.CreateAsync(factory, "papel-cruza-b");

            var responses = await Task.WhenAll(DeactivateAsync(a, b.User.Id), ChangeAsync(b, a.User.Id, "leitor"));

            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Forbidden], responses.Select(response => response.StatusCode).Order().ToList());
            var users = new[] { await SignedInUser.ReloadAsync(factory.Services, a.User.Id), await SignedInUser.ReloadAsync(factory.Services, b.User.Id) };
            Assert.Equal(1, users.Count(user => user.IsActive && user.Role == UserRole.Admin));
        }
    }

    /// <summary>
    /// A ordem que a corrida acima só acha por sorte, forçada: o teste segura o <c>BUTECOD1</c> numa
    /// conexão própria, a desativação passa pelo <c>FindAdminAsync</c> e espera o lock, quem chama é
    /// rebaixado direto no banco nesse meio tempo, e o lock é solto. Só a releitura de quem chama
    /// <b>Admin</b> ativo dentro do lock recusa; conferindo só "ativo", ela desativaria o alvo.
    /// </summary>
    [Theory]
    [InlineData("deactivate")]
    [InlineData("role")]
    public async Task AWriteWaitingOnTheLock_RereadsTheCallerAsAdmin(string write)
    {
        var caller = await SignedInUser.CreateAsync(factory, $"papel-espera-{write}");
        var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("papel-espera-alvo"), SignedInUser.DefaultPassword);

        await using var holder = new Npgsql.NpgsqlConnection(factory.PostgresConnectionString);
        await holder.OpenAsync();
        await using var holding = await holder.BeginTransactionAsync();
        await using (var take = new Npgsql.NpgsqlCommand($"SELECT pg_advisory_xact_lock({UserAdministration.AdvisoryLockKey})", holder, holding))
        {
            await take.ExecuteNonQueryAsync();
        }

        var pending = write == "deactivate" ? DeactivateAsync(caller, target.Id) : ChangeAsync(caller, target.Id, "leitor");
        await WaitForAnotherSessionOnTheLockAsync(holder);
        await LocalUserSeed.SetRoleAsync(factory.Services, caller.User.Id, UserRole.Operator);
        await holding.CommitAsync();

        var response = await pending;
        var after = await SignedInUser.ReloadAsync(factory.Services, target.Id);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(after.IsActive);
        Assert.Equal(UserRole.Admin, after.Role);
    }

    private static async Task WaitForAnotherSessionOnTheLockAsync(Npgsql.NpgsqlConnection holder)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            await using var waiting = new Npgsql.NpgsqlCommand("SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted", holder);
            if ((long)(await waiting.ExecuteScalarAsync())! > 0)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("nenhuma escrita chegou a esperar o lock BUTECOD1");
    }

    private async Task<int> FailuresOfAsync(string normalizedEmail)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LoginThrottles.AsNoTracking()
            .SingleOrDefaultAsync(throttle => throttle.NormalizedEmail == normalizedEmail);
        return row?.ConsecutiveFailures ?? 0;
    }

    private static string Pad(string part)
    {
        var text = part.Replace('-', '+').Replace('_', '/');
        return text.PadRight(text.Length + (4 - text.Length % 4) % 4, '=');
    }
}
