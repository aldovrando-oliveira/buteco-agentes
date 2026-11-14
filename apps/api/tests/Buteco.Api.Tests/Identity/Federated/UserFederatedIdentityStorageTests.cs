using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users.Federated;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Identity.Federated;

/// <summary>
/// A unicidade do vínculo no banco (spec <c>federated-login</c>, "O vínculo pelo par (issuer, sub)"),
/// gravando direto pelo <c>DbContext</c>. Guarda verificado contra o defeito (tasks.md, 2.3): sem o
/// índice único (issuer, subject) na migration, a segunda gravação passa.
/// </summary>
public class UserFederatedIdentityStorageTests(LocalUsersFixture fixture) : IClassFixture<LocalUsersFixture>
{
    private const string Issuer = "https://accounts.google.com/";

    [Fact]
    public async Task OneProviderAccount_LinksOneUserOnly()
    {
        var first = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("vinculo-a"), "senha-de-teste-123");
        var second = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("vinculo-b"), "senha-de-teste-123");
        var subject = $"sub-{Guid.NewGuid():N}";

        await SaveAsync(UserFederatedIdentity.Connect(first.Id, Issuer, subject, "a@exemplo.test", DateTimeOffset.UtcNow));
        var exception = await Record.ExceptionAsync(() =>
            SaveAsync(UserFederatedIdentity.Connect(second.Id, Issuer, subject, "b@exemplo.test", DateTimeOffset.UtcNow)));

        Assert.IsType<DbUpdateException>(exception);
    }

    [Fact]
    public async Task OneUser_LinksOneAccountPerIssuer()
    {
        var user = await LocalUserSeed.CreateUserAsync(fixture.Services, LocalUserSeed.UniqueEmail("vinculo-c"), "senha-de-teste-123");

        await SaveAsync(UserFederatedIdentity.Connect(user.Id, Issuer, $"sub-{Guid.NewGuid():N}", null, DateTimeOffset.UtcNow));
        var exception = await Record.ExceptionAsync(() =>
            SaveAsync(UserFederatedIdentity.Connect(user.Id, Issuer, $"sub-{Guid.NewGuid():N}", null, DateTimeOffset.UtcNow)));

        Assert.IsType<DbUpdateException>(exception);
    }

    private async Task SaveAsync(UserFederatedIdentity identity)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserFederatedIdentities.Add(identity);
        await db.SaveChangesAsync();
    }
}
