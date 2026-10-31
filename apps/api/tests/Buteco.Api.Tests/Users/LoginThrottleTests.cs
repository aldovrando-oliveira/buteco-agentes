using System.Net;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Users;

/// <summary>
/// O bloqueio na rota, com o banco (spec <c>local-users</c>, "Bloqueio progressivo por
/// e-mail, exista ou não a conta"; design.md da change usuarios-locais-login-emissor, D4).
/// O relógio é o <see cref="LocalUsersFixture.Clock"/>, compartilhado pela classe e que só
/// anda para a frente; cada caso usa um e-mail próprio.
///
/// <para>
/// Verificado contra o defeito (convenção 15): com a falha contada só quando a conta existe
/// (a chave pela linha do usuário), o caso <see cref="UnknownEmail_BlocksTheSame"/> reprova
/// (registro no tasks.md, 5.4).
/// </para>
/// </summary>
public class LoginThrottleTests(LocalUsersFixture factory) : IClassFixture<LocalUsersFixture>
{
    private const string Password = "senha-certa-do-teste";

    private HttpClient Client() => factory.CreateClient(new() { HandleCookies = false });

    private async Task FailAsync(string email, int times)
    {
        for (var i = 0; i < times; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LocalUserSeed.PostSessionAsync(Client(), email, "errada")).StatusCode);
        }
    }

    private static int RetryAfter(HttpResponseMessage response) => int.Parse(response.Headers.GetValues("Retry-After").Single());

    [Fact]
    public async Task FiveFailures_TheSixthIs423_EvenWithTheRightPassword()
    {
        var email = LocalUserSeed.UniqueEmail("cinco");
        await LocalUserSeed.CreateUserAsync(factory.Services, email, Password);

        await FailAsync(email, 5);
        var sixth = await LocalUserSeed.PostSessionAsync(Client(), email, Password);

        Assert.Equal(HttpStatusCode.Locked, sixth.StatusCode);
        Assert.InRange(RetryAfter(sixth), 1, 300);
        Assert.False(sixth.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task UnknownEmail_BlocksTheSame()
    {
        var email = LocalUserSeed.UniqueEmail("sem-conta");

        await FailAsync(email, 5);

        Assert.Equal(HttpStatusCode.Locked, (await LocalUserSeed.PostSessionAsync(Client(), email, "qualquer")).StatusCode);
    }

    [Fact]
    public async Task AfterTheLockExpires_OneFailureLocksForDouble()
    {
        var email = LocalUserSeed.UniqueEmail("reincide");
        await LocalUserSeed.CreateUserAsync(factory.Services, email, Password);
        await FailAsync(email, 5);

        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        await FailAsync(email, 1);
        var next = await LocalUserSeed.PostSessionAsync(Client(), email, Password);

        Assert.Equal(HttpStatusCode.Locked, next.StatusCode);
        Assert.InRange(RetryAfter(next), 301, 600);
    }

    [Fact]
    public async Task AttemptsDuringTheLock_DoNotExtendIt()
    {
        var email = LocalUserSeed.UniqueEmail("durante");
        await FailAsync(email, 5);

        var first = await LocalUserSeed.PostSessionAsync(Client(), email, "errada");
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var later = await LocalUserSeed.PostSessionAsync(Client(), email, "errada");

        Assert.Equal(HttpStatusCode.Locked, first.StatusCode);
        Assert.Equal(HttpStatusCode.Locked, later.StatusCode);
        Assert.True(RetryAfter(later) <= RetryAfter(first) - 59);
    }

    [Fact]
    public async Task Success_ZeroesTheCount()
    {
        var email = LocalUserSeed.UniqueEmail("zera");
        await LocalUserSeed.CreateUserAsync(factory.Services, email, Password);

        await FailAsync(email, 4);
        Assert.Equal(HttpStatusCode.NoContent, (await LocalUserSeed.PostSessionAsync(Client(), email, Password)).StatusCode);
        await FailAsync(email, 4);

        Assert.Equal(HttpStatusCode.NoContent, (await LocalUserSeed.PostSessionAsync(Client(), email, Password)).StatusCode);
    }
}
