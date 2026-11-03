using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Buteco.Api.Users.AccessLinks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users.AccessLinks;

/// <summary>
/// O convidado nas rotas de estado da #219 (spec <c>user-management</c>; design.md da change
/// convites-redefinicao-senha, D2).
///
/// <para>
/// Verificado contra o defeito (tasks.md, 4.1), um de cada vez: <c>activate</c> sem a recusa →
/// <see cref="Activate_OnTheInvited_Is409_AndItStaysInvited"/> reprova (sai <c>200</c>, ativo e sem
/// senha); <c>deactivate</c> sem a recusa → <see cref="Deactivate_OnTheInvited_Is409_AndTheLinkStays"/>
/// reprova (sai <c>200</c>, desativado e sem link).
/// </para>
///
/// <para>
/// E o cancelamento (tasks.md, 5.2): o <c>DELETE</c> sem <c>status = 'invited'</c> →
/// <see cref="CancellingAnAcceptedInvitation_Is409_AndTheUserStaysActive"/> reprova (apaga o ativo).
/// </para>
/// </summary>
public class InvitationLifecycleTests(InvitationLifecycleTests.Fixture factory) : IClassFixture<InvitationLifecycleTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private Task<HttpResponseMessage> PostAsync(SignedInUser caller, string path, object? body = null) =>
        TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Post, path, caller.AccessToken, body);

    [Fact]
    public async Task Activate_OnTheInvited_Is409_AndItStaysInvited()
    {
        var caller = await SignedInUser.CreateAsync(factory, "ativa-convidado");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);

        var response = await PostAsync(caller, $"/users/{invited.User.Id}/activate");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var reloaded = await SignedInUser.ReloadAsync(factory.Services, invited.User.Id);
        Assert.Equal(UserStatus.Invited, reloaded.Status);
        Assert.Null(reloaded.PasswordHash);
    }

    [Fact]
    public async Task Deactivate_OnTheInvited_Is409_AndTheLinkStays()
    {
        var caller = await SignedInUser.CreateAsync(factory, "desativa-convidado");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);

        var response = await PostAsync(caller, $"/users/{invited.User.Id}/deactivate");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var reloaded = await SignedInUser.ReloadAsync(factory.Services, invited.User.Id);
        Assert.Equal(UserStatus.Invited, reloaded.Status);
        Assert.Equal(invited.User.AccessLinkHash, reloaded.AccessLinkHash);
    }

    [Fact]
    public async Task RenewingAnExpiredInvitation_IssuesANew72HourLink_AndTheOldHashIsGone()
    {
        var caller = await SignedInUser.CreateAsync(factory, "renova-vencido");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id, expiresAt: factory.Clock.GetUtcNow().AddMinutes(-1));

        var response = await AccessLinkClient.RenewAsync(SignedInUser.Anonymous(factory), caller, invited.User.Id, caller.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await AccessLinkClient.JsonAsync(response);
        Assert.Equal(factory.Clock.GetUtcNow() + AccessLinkToken.InvitationLifetime, body.GetProperty("expiraEm").GetDateTimeOffset());
        var reloaded = await SignedInUser.ReloadAsync(factory.Services, invited.User.Id);
        Assert.Equal(AccessLinkToken.Hash(AccessLinkClient.TokenOf(body.GetProperty("link").GetString()!)), reloaded.AccessLinkHash);
        Assert.NotEqual(invited.User.AccessLinkHash, reloaded.AccessLinkHash);
    }

    [Fact]
    public async Task RenewingAnActive_Is409()
    {
        var caller = await SignedInUser.CreateAsync(factory, "renova-ativo");
        var active = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("ativo"), SignedInUser.DefaultPassword);

        var response = await AccessLinkClient.RenewAsync(SignedInUser.Anonymous(factory), caller, active.Id, caller.Password);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Null((await SignedInUser.ReloadAsync(factory.Services, active.Id)).AccessLinkHash);
    }

    [Fact]
    public async Task Cancelling_DeletesTheInvited_AndFreesTheEmail()
    {
        var caller = await SignedInUser.CreateAsync(factory, "cancela");
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);
        var client = SignedInUser.Anonymous(factory);

        Assert.Equal(HttpStatusCode.NoContent, (await AccessLinkClient.CancelAsync(client, caller, invited.User.Id)).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync(user => user.Id == invited.User.Id));
        }

        Assert.Equal(HttpStatusCode.Created, (await AccessLinkClient.InviteAsync(client, caller, "De Novo", invited.User.Email, caller.Password)).StatusCode);
    }

    [Fact]
    public async Task CancellingAnAcceptedInvitation_Is409_AndTheUserStaysActive()
    {
        var caller = await SignedInUser.CreateAsync(factory, "cancela-aceito");
        var active = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("ja-aceitou"), SignedInUser.DefaultPassword);

        var response = await AccessLinkClient.CancelAsync(SignedInUser.Anonymous(factory), caller, active.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserStatus.Active, (await SignedInUser.ReloadAsync(factory.Services, active.Id)).Status);
    }

    [Fact]
    public async Task UnknownId_Is404_OnRenewAndCancel()
    {
        var caller = await SignedInUser.CreateAsync(factory, "convite-404");
        var client = SignedInUser.Anonymous(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await AccessLinkClient.RenewAsync(client, caller, Guid.NewGuid(), caller.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AccessLinkClient.CancelAsync(client, caller, Guid.NewGuid())).StatusCode);
    }
}
