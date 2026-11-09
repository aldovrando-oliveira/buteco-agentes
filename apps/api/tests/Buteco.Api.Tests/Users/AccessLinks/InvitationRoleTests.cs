using System.Net;
using System.Text;
using System.Text.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users.AccessLinks;

/// <summary>
/// "Convidar cria o usuário no estado convidado" (spec <c>user-access-links</c>; design.md da change
/// papeis-no-token, D5, Q9): o <c>papel</c> é obrigatório, recusado com <c>400</c> antes da senha e
/// sem contar no bloqueio de quem chama, e o aceite mantém o papel do convite até o token.
/// </summary>
public class InvitationRoleTests(InvitationRoleTests.Fixture factory) : IClassFixture<InvitationRoleTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private HttpClient Client => SignedInUser.Anonymous(factory);

    [Theory]
    [InlineData(null)]
    [InlineData("superusuario")]
    public async Task AnInvitationWithoutAValidRole_Is400_WritesNothing_AndCountsNoFailure(string? papel)
    {
        var caller = await SignedInUser.CreateAsync(factory, "convite-sem-papel");
        var email = LocalUserSeed.UniqueEmail("convidado-sem-papel");

        var response = await AccessLinkClient.InviteAsync(Client, caller, "Sem Papel", email, "senha-errada-de-proposito", papel);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Users.AnyAsync(user => user.NormalizedEmail == email));
        Assert.False(await db.LoginThrottles.AnyAsync(throttle => throttle.NormalizedEmail == caller.User.NormalizedEmail));
    }

    [Fact]
    public async Task AReaderInvitation_IsListedAsReader_AndTheAcceptedTokenCarriesReader()
    {
        var caller = await SignedInUser.CreateAsync(factory, "convite-leitor");
        var email = LocalUserSeed.UniqueEmail("convidado-leitor");

        var created = await AccessLinkClient.InviteAsync(Client, caller, "Leitora", email, caller.Password, "leitor");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("leitor", (await AccessLinkClient.JsonAsync(created)).GetProperty("usuario").GetProperty("papel").GetString());
        var accepted = await AccessLinkClient.AcceptAsync(Client, await AccessLinkClient.TokenAsync(created), "senha-da-leitora-convidada");
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);

        var tokens = await IdentityIssuerUserFlow.SignInAsync(Client, factory.Services, email, "senha-da-leitora-convidada");

        var payload = tokens.AccessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
        var claims = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=')))).RootElement;
        Assert.Equal("reader", claims.GetProperty("role").GetString());
    }
}
