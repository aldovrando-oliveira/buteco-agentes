using System.Net;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users.Login;

namespace Buteco.Api.Tests.Users;

/// <summary>
/// O usuário desativado no login (spec <c>local-users</c>, "Resposta de login não revela se
/// a conta existe" e "Desativado com a senha certa não mexe na contagem"; design.md da
/// change usuarios-locais-login-emissor, D4). A parte do refresh e do cookie em voo está
/// em <c>Identity/UserDeactivationFlowTests</c>.
///
/// <para>
/// Verificado contra o defeito (convenção 15): com o <c>403</c> tratado como sucesso no
/// bloqueio (zerando a linha), o caso
/// <see cref="Deactivated_RightPassword_NeitherCountsNorZeroes"/> reprova (registro no
/// tasks.md, 5.4).
/// </para>
/// </summary>
public class UserDeactivationTests(LocalUsersFixture factory) : IClassFixture<LocalUsersFixture>
{
    private const string Password = "senha-certa-do-teste";

    private HttpClient Client() => factory.CreateClient(new() { HandleCookies = false });

    private async Task<string> DeactivatedUserAsync()
    {
        var email = LocalUserSeed.UniqueEmail("desativado");
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, email, Password);
        await LocalUserSeed.DeactivateAsync(factory.Services, user.Id);
        return email;
    }

    [Fact]
    public async Task Deactivated_RightPassword_Is403WithTheMessage_AndNoCookie()
    {
        var email = await DeactivatedUserAsync();

        var response = await LocalUserSeed.PostSessionAsync(Client(), email, Password);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(SessionEndpoints.DeactivatedMessage, await response.Content.ReadAsStringAsync());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Deactivated_WrongPassword_AnswersLikeAnActiveUser()
    {
        var deactivated = await DeactivatedUserAsync();
        var active = LocalUserSeed.UniqueEmail("ativo");
        await LocalUserSeed.CreateUserAsync(factory.Services, active, Password);

        var fromDeactivated = await LocalUserSeed.PostSessionAsync(Client(), deactivated, "errada");
        var fromActive = await LocalUserSeed.PostSessionAsync(Client(), active, "errada");

        Assert.Equal(HttpStatusCode.Unauthorized, fromDeactivated.StatusCode);
        Assert.Equal(await fromActive.Content.ReadAsStringAsync(), await fromDeactivated.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Deactivated_RightPassword_NeitherCountsNorZeroes()
    {
        var email = await DeactivatedUserAsync();
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LocalUserSeed.PostSessionAsync(Client(), email, "errada")).StatusCode);
        }

        // Não contam: se contassem, a segunda já seria 423.
        Assert.Equal(HttpStatusCode.Forbidden, (await LocalUserSeed.PostSessionAsync(Client(), email, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await LocalUserSeed.PostSessionAsync(Client(), email, Password)).StatusCode);

        // Não zeram: a próxima falha é a 5ª e bloqueia.
        Assert.Equal(HttpStatusCode.Unauthorized, (await LocalUserSeed.PostSessionAsync(Client(), email, "errada")).StatusCode);
        Assert.Equal(HttpStatusCode.Locked, (await LocalUserSeed.PostSessionAsync(Client(), email, Password)).StatusCode);
    }
}
