using System.Net;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Users.Management;

/// <summary>
/// "Reativar usuário" (spec <c>user-management</c>; design.md da change rotas-conta-usuarios,
/// D7): volta o estado, a senha e as 2 etapas; não voltam as sessões de antes.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 6.8: com a reativação sem
/// mudar o estado, <see cref="Reactivate_LoginComesBack_TheOldSessionsDoNot"/> reprova.
/// </para>
/// </summary>
public class UserActivationTests(UserActivationTests.Fixture factory) : IClassFixture<UserActivationTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private Task<HttpResponseMessage> PostAsync(SignedInUser caller, string path) =>
        TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Post, path, caller.AccessToken);

    [Fact]
    public async Task Reactivate_LoginComesBack_TheOldSessionsDoNot()
    {
        var caller = await SignedInUser.CreateAsync(factory, "reativa-quem-chama");
        var target = await SignedInUser.CreateAsync(factory, "reativa-alvo");
        await TwoFactorTestClient.SeedEnabledAsync(factory.Services, target.User.Id);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(caller, $"/users/{target.User.Id}/deactivate")).StatusCode);

        var response = await PostAsync(caller, $"/users/{target.User.Id}/activate");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await TwoFactorTestClient.JsonAsync(response);
        Assert.Equal("ativo", body.GetProperty("estado").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, body.GetProperty("desativadoEm").ValueKind);
        Assert.False(await SignedInUser.RenewsAsync(factory, target.RefreshToken));
        var (login, _) = await TwoFactorTestClient.PasswordStepAsync(SignedInUser.Anonymous(factory), target.User.Email, target.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await SignedInUser.ReloadAsync(factory.Services, target.User.Id)).IsTwoFactorEnabled);
    }

    [Fact]
    public async Task AfterReactivation_ANewSessionRenews()
    {
        var caller = await SignedInUser.CreateAsync(factory, "reativa-nova");
        var target = await SignedInUser.CreateAsync(factory, "reativa-nova-alvo");
        await PostAsync(caller, $"/users/{target.User.Id}/deactivate");
        await PostAsync(caller, $"/users/{target.User.Id}/activate");

        var fresh = await target.SignInAgainAsync(factory);

        Assert.True(await SignedInUser.RenewsAsync(factory, fresh.RefreshToken));
    }

    [Fact]
    public async Task ReactivateSomeoneActive_Is200_AndNothingChanges()
    {
        var caller = await SignedInUser.CreateAsync(factory, "ja-ativo-quem-chama");
        var target = await SignedInUser.CreateAsync(factory, "ja-ativo");
        var stamp = (await SignedInUser.ReloadAsync(factory.Services, target.User.Id)).SecurityStamp;

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(caller, $"/users/{target.User.Id}/activate")).StatusCode);

        Assert.Equal(stamp, (await SignedInUser.ReloadAsync(factory.Services, target.User.Id)).SecurityStamp);
        Assert.True(await SignedInUser.RenewsAsync(factory, target.RefreshToken));
    }

    [Fact]
    public async Task UnknownUser_Is404()
    {
        var caller = await SignedInUser.CreateAsync(factory, "reativa-404");

        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(caller, $"/users/{Guid.NewGuid()}/activate")).StatusCode);
    }
}
