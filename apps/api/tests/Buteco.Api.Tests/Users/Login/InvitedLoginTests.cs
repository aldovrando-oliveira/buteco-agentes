using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users.Login;
using Buteco.Api.Users.Passwords;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Buteco.Api.Tests.Users.Login;

/// <summary>
/// O convidado no login (spec <c>local-users</c>, "Resposta de login não revela se a conta existe";
/// design.md da change convites-redefinicao-senha, D2). O guarda confere o caminho (a verificação
/// contra o hash fictício chamada, pelo <see cref="CountingPasswordHasher"/>), e não o tempo, que
/// reprova sob carga (#226); a comparação de tempo é conferência manual no fechamento.
///
/// <para>
/// Verificado contra o defeito (tasks.md, 6.10), um de cada vez: o <c>return Failed</c> imediato do
/// <c>Verify</c> → o contador fica em zero e <see cref="TheInvited_GetsTheSameAnswerAsAnUnknownEmail_AfterTheDummyVerification"/>
/// reprova; uma recusa pelo estado antes da senha → o mesmo caso reprova (o corpo difere).
/// </para>
/// </summary>
public class InvitedLoginTests(InvitedLoginTests.Fixture factory) : IClassFixture<InvitedLoginTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture
    {
        public CountingPasswordHasher Hasher { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<PasswordHasher>();
                services.AddSingleton<PasswordHasher>(Hasher);
            });
        }
    }

    private async Task<int?> FailuresOfAsync(string normalizedEmail)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().LoginThrottles
            .Where(row => row.NormalizedEmail == normalizedEmail).Select(row => (int?)row.ConsecutiveFailures).SingleOrDefaultAsync();
    }

    [Fact]
    public async Task TheInvited_GetsTheSameAnswerAsAnUnknownEmail_AfterTheDummyVerification()
    {
        var inviter = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("convidou"), SignedInUser.DefaultPassword);
        var invited = await InvitationSeed.InvitedAsync(factory.Services, inviter.Id);
        var client = SignedInUser.Anonymous(factory);

        factory.Hasher.Reset();
        var forInvited = await LocalUserSeed.PostSessionAsync(client, invited.User.Email, "qualquer-senha-de-teste");
        var dummyForInvited = factory.Hasher.DummyVerifications;

        factory.Hasher.Reset();
        var forUnknown = await LocalUserSeed.PostSessionAsync(client, LocalUserSeed.UniqueEmail("sem-conta"), "qualquer-senha-de-teste");
        var dummyForUnknown = factory.Hasher.DummyVerifications;

        Assert.Equal(HttpStatusCode.Unauthorized, forInvited.StatusCode);
        Assert.Equal(await forUnknown.Content.ReadAsStringAsync(), await forInvited.Content.ReadAsStringAsync());
        Assert.Contains(SessionEndpoints.InvalidCredentialsMessage, await forInvited.Content.ReadAsStringAsync());
        Assert.Equal(1, dummyForUnknown);
        Assert.Equal(1, dummyForInvited);
        Assert.Equal(1, await FailuresOfAsync(invited.User.NormalizedEmail));
    }
}
