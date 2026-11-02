using System.Net;
using System.Text.Json;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Users.Management;

/// <summary>
/// "Lista de usuários" (spec <c>user-management</c>; design.md da change rotas-conta-usuarios,
/// D6). O desempate pelo id segue a quinta forma da convenção 15: o comportamental mais a
/// asserção sobre o SQL emitido.
///
/// <para>
/// Verificado contra o defeito, registro no tasks.md, 6.2: sem o <c>ThenBy</c> do id,
/// <see cref="SameName_TieBreaksById_AndTheQueryEndsTheOrderOnTheId"/> reprova pela asserção
/// do SQL, em toda execução da classe.
/// </para>
/// </summary>
public class UserListTests(UserListTests.Fixture factory) : IClassFixture<UserListTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private async Task<List<JsonElement>> ListAsync(SignedInUser caller)
    {
        var response = await TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Get, "/users", caller.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await TwoFactorTestClient.JsonAsync(response)).EnumerateArray().ToList();
    }

    [Fact]
    public async Task ActiveAndDeactivated_BothListed_WithTheirFields()
    {
        var caller = await SignedInUser.CreateAsync(factory, "lista-quem-chama");
        var withTwoFactor = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("lista-2e"), SignedInUser.DefaultPassword);
        await TwoFactorTestClient.SeedEnabledAsync(factory.Services, withTwoFactor.Id);
        var deactivated = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("lista-desativado"), SignedInUser.DefaultPassword);
        await LocalUserSeed.DeactivateAsync(factory.Services, deactivated.Id);

        var users = await ListAsync(caller);

        var first = Assert.Single(users, user => user.GetProperty("id").GetGuid() == withTwoFactor.Id);
        Assert.Equal("ativo", first.GetProperty("estado").GetString());
        Assert.True(first.GetProperty("duasEtapas").GetBoolean());
        Assert.Equal(["senha"], first.GetProperty("formasDeEntrada").EnumerateArray().Select(form => form.GetString()));
        Assert.Equal(withTwoFactor.Email, first.GetProperty("email").GetString());
        var second = Assert.Single(users, user => user.GetProperty("id").GetGuid() == deactivated.Id);
        Assert.Equal("desativado", second.GetProperty("estado").GetString());
        Assert.False(second.GetProperty("duasEtapas").GetBoolean());
    }

    [Fact]
    public async Task SameName_TieBreaksById_AndTheQueryEndsTheOrderOnTheId()
    {
        var caller = await SignedInUser.CreateAsync(factory, "lista-desempate");
        var name = $"Mesmo Nome {Guid.NewGuid():N}";
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add((await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("homonimo"), SignedInUser.DefaultPassword, name)).Id);
        }

        List<JsonElement> users = [];
        var commands = await factory.SqlCapture.CaptureAsync(async () => users = await ListAsync(caller));

        // O SQL primeiro: é a metade que reprova sempre sem o ThenBy; a ordem observada pode calhar.
        var query = EmittedSqlCapture.SingleCommandContaining(commands, "FROM users", "ORDER BY");
        EmittedSqlCapture.AssertOrderByEndsWithTieBreak(query, "u.id");
        var listed = users.Where(user => user.GetProperty("nome").GetString() == name).Select(user => user.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(ids.Order().ToList(), listed);
    }
}
