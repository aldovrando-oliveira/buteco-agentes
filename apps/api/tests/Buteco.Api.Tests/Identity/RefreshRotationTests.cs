using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.EntityFrameworkCore.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// Refresh rotativo, reuso e último acesso (specs <c>issuer-user-login</c>, "Refresh
/// rotativo e deslizante de 14 dias", e <c>local-users</c>, "Último acesso registrado no
/// login e na renovação"; design.md da change usuarios-locais-login-emissor, D3.7 e D3.11).
/// </summary>
public class RefreshRotationTests(LocalUsersFixture factory) : IClassFixture<LocalUsersFixture>
{
    private const string Password = "senha-certa-do-teste";

    private async Task<(Buteco.Api.Users.User User, IdentityIssuerUserFlow.Tokens Tokens)> SignInAsync()
    {
        var email = LocalUserSeed.UniqueEmail("refresh");
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, email, Password);
        return (user, await IdentityIssuerUserFlow.SignInAsync(IdentityIssuerUserFlow.Client(factory), factory.Services, email, Password));
    }

    private async Task<DateTimeOffset?> LastAccessAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(user => user.Id == userId)).LastAccessAt;
    }

    [Fact]
    public async Task Refresh_IssuesANewPair_AndThePresentedOneIsRedeemed()
    {
        var (_, tokens) = await SignInAsync();
        var client = IdentityIssuerUserFlow.Client(factory);

        var renewed = await IdentityIssuerUserFlow.ReadTokensAsync(await IdentityIssuerUserFlow.RefreshAsync(client, tokens.RefreshToken));

        Assert.NotEqual(tokens.RefreshToken, renewed.RefreshToken);
        Assert.NotEqual(tokens.AccessToken, renewed.AccessToken);

        using var scope = factory.Services.CreateScope();
        var statuses = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<OpenIddictEntityFrameworkCoreToken>()
            .Where(token => token.Type == TokenTypeIdentifiers.RefreshToken)
            .Select(token => token.Status)
            .ToListAsync();
        Assert.Contains(Statuses.Redeemed, statuses);
    }

    [Fact]
    public async Task ReuseAfterTheLeeway_RevokesTheChain()
    {
        var (_, tokens) = await SignInAsync();
        var client = IdentityIssuerUserFlow.Client(factory);
        var renewed = await IdentityIssuerUserFlow.ReadTokensAsync(await IdentityIssuerUserFlow.RefreshAsync(client, tokens.RefreshToken));

        // Fora da tolerância de reuso do OpenIddict (30 s): o refresh antigo de novo
        // revoga a cadeia, e o novo também deixa de valer.
        factory.Clock.Advance(TimeSpan.FromSeconds(31));
        var reuse = await IdentityIssuerUserFlow.RefreshAsync(client, tokens.RefreshToken);
        var afterReuse = await IdentityIssuerUserFlow.RefreshAsync(client, renewed.RefreshToken);

        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, afterReuse.StatusCode);
    }

    [Fact]
    public async Task LastAccess_ChangesOnLoginAndOnRefresh_AndNotOnAnApiCall()
    {
        var (user, tokens) = await SignInAsync();
        var afterLogin = await LastAccessAsync(user.Id);
        Assert.NotNull(afterLogin);

        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var client = IdentityIssuerUserFlow.Client(factory);
        var renewed = await IdentityIssuerUserFlow.ReadTokensAsync(await IdentityIssuerUserFlow.RefreshAsync(client, tokens.RefreshToken));
        var afterRefresh = await LastAccessAsync(user.Id);
        Assert.True(afterRefresh > afterLogin);

        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var api = new HttpRequestMessage(HttpMethod.Get, "/agents");
        api.Headers.Authorization = new("Bearer", renewed.AccessToken);
        await client.SendAsync(api);

        Assert.Equal(afterRefresh, await LastAccessAsync(user.Id));
    }
}
