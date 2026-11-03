using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users;

/// <summary>
/// O texto de <c>users.status</c> (spec <c>local-users</c>; design.md da change
/// convites-redefinicao-senha, D2): <c>invited</c> é lido como convidado, e texto desconhecido
/// falha na leitura em vez de virar desativado.
///
/// <para>
/// Verificado contra o defeito (tasks.md, 3.1): com a conversão de antes
/// (<c>value == "active" ? Active : Deactivated</c>), os dois casos reprovam.
/// </para>
/// </summary>
public class UserStatusConversionTests(UserStatusConversionTests.Fixture factory) : IClassFixture<UserStatusConversionTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private async Task<Guid> UserWithStatusAsync(string status)
    {
        var user = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("estado"), SignedInUser.DefaultPassword);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .ExecuteSqlInterpolatedAsync($"UPDATE users SET status = {status} WHERE id = {user.Id}");
        return user.Id;
    }

    [Fact]
    public async Task Invited_IsReadAsInvited()
    {
        var id = await UserWithStatusAsync("invited");

        Assert.Equal(UserStatus.Invited, (await SignedInUser.ReloadAsync(factory.Services, id)).Status);
    }

    [Fact]
    public async Task UnknownText_FailsTheRead_CitingTheValue()
    {
        var id = await UserWithStatusAsync("qualquer");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => SignedInUser.ReloadAsync(factory.Services, id));

        Assert.Contains("'qualquer'", exception.Message, StringComparison.Ordinal);
    }
}
