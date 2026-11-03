using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users.AccessLinks;

/// <summary>
/// A senha de quem chama (spec <c>user-access-links</c>, "Criar convite, gerar novo link e gerar
/// redefinição pedem a senha de quem chama"; design.md da change convites-redefinicao-senha, D1,
/// P10).
///
/// <para>
/// Verificado contra o defeito (tasks.md, 5.5), um de cada vez: a conferência retirada de criar,
/// de renovar e de gerar redefinição → o caso da rota em <see cref="WithoutOrWithAWrongPassword_Is401_AndNothingIsWritten"/>
/// reprova; a falha contada no e-mail do alvo → <see cref="TheFailure_CountsOnTheCallersEmail_NotTheTargets"/>
/// reprova.
/// </para>
/// </summary>
public class AccessLinkCallerPasswordTests(AccessLinkCallerPasswordTests.Fixture factory) : IClassFixture<AccessLinkCallerPasswordTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private HttpClient Client => SignedInUser.Anonymous(factory);

    private async Task<int?> FailuresOfAsync(string normalizedEmail)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().LoginThrottles
            .Where(row => row.NormalizedEmail == normalizedEmail)
            .Select(row => (int?)row.ConsecutiveFailures)
            .SingleOrDefaultAsync();
    }

    private async Task<int> UsersNamedAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync(user => user.Name == name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("senha-errada-de-proposito")]
    public async Task WithoutOrWithAWrongPassword_Is401_AndNothingIsWritten(string? password)
    {
        var caller = await SignedInUser.CreateAsync(factory, "senha-quem-chama");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);
        var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("alvo"), SignedInUser.DefaultPassword);
        var name = $"Nao Gravado {Guid.NewGuid():N}";

        var create = await AccessLinkClient.InviteAsync(Client, caller, name, LocalUserSeed.UniqueEmail("nao-gravado"), password);
        var renew = await AccessLinkClient.RenewAsync(Client, caller, invited.User.Id, password);
        var reset = await AccessLinkClient.ResetAsync(Client, caller, target.Id, password);

        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, renew.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, reset.StatusCode);
        Assert.Equal("Senha inválida.", (await AccessLinkClient.JsonAsync(create)).GetProperty("title").GetString());
        Assert.Equal(0, await UsersNamedAsync(name));
        Assert.Equal(invited.User.AccessLinkHash, (await SignedInUser.ReloadAsync(factory.Services, invited.User.Id)).AccessLinkHash);
        Assert.Null((await SignedInUser.ReloadAsync(factory.Services, target.Id)).AccessLinkHash);
    }

    [Fact]
    public async Task TheFailure_CountsOnTheCallersEmail_NotTheTargets()
    {
        var caller = await SignedInUser.CreateAsync(factory, "conta-quem-chama");
        var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("nao-conta"), SignedInUser.DefaultPassword);

        await AccessLinkClient.ResetAsync(Client, caller, target.Id, "senha-errada-de-proposito");

        Assert.Equal(1, await FailuresOfAsync(caller.User.NormalizedEmail));
        Assert.Null(await FailuresOfAsync(target.NormalizedEmail));
    }

    [Fact]
    public async Task FiveFailures_Lock_AndTheSixthWithTheRightPassword_Is423_WithoutWriting()
    {
        var caller = await SignedInUser.CreateAsync(factory, "bloqueia-quem-chama");
        var name = $"Bloqueado {Guid.NewGuid():N}";
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await AccessLinkClient.InviteAsync(Client, caller, name, LocalUserSeed.UniqueEmail("tentativa"), "senha-errada-de-proposito")).StatusCode);
        }

        var sixth = await AccessLinkClient.InviteAsync(Client, caller, name, LocalUserSeed.UniqueEmail("sexta"), caller.Password);

        Assert.Equal((HttpStatusCode)423, sixth.StatusCode);
        Assert.True(sixth.Headers.Contains("Retry-After"));
        Assert.Equal(0, await UsersNamedAsync(name));
    }

    [Fact]
    public async Task TheRightPassword_Passes_OnTheThreeRoutes()
    {
        var caller = await SignedInUser.CreateAsync(factory, "acerta-senha");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);
        var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("alvo-certo"), SignedInUser.DefaultPassword);

        Assert.Equal(HttpStatusCode.Created, (await AccessLinkClient.InviteAsync(Client, caller, "Certo", LocalUserSeed.UniqueEmail("certo"), caller.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AccessLinkClient.RenewAsync(Client, caller, invited.User.Id, caller.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AccessLinkClient.ResetAsync(Client, caller, target.Id, caller.Password)).StatusCode);
    }

    [Fact]
    public async Task Cancelling_DoesNotAskForThePassword()
    {
        var caller = await SignedInUser.CreateAsync(factory, "cancela-sem-senha");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await AccessLinkClient.CancelAsync(Client, caller, invited.User.Id)).StatusCode);
    }
}
