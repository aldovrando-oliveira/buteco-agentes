using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users.Management;

/// <summary>
/// "Editar nome e e-mail" (spec <c>user-management</c>; design.md da change
/// rotas-conta-usuarios, D5). A corrida segue a quinta forma da convenção 15: o comportamental
/// (<c>Task.WhenAll</c>) pode passar por sorte, e o caso determinístico põe a outra escrita
/// entre a conferência e o <c>UPDATE</c>, por um interceptor.
///
/// <para>
/// Verificado contra o defeito, registro no tasks.md, 6.4 e 6.5:
/// <list type="bullet">
/// <item>conferência por <c>Email</c> no lugar de <c>NormalizedEmail</c> →
/// <see cref="SomeoneElsesEmail_InAnotherCase_Is409"/> reprova (o índice responde <c>500</c>);</item>
/// <item>troca do stamp introduzida na edição → <see cref="ChangingTheEmail_MovesTheLogin_AndKeepsTheSub_AndTheSessions"/> reprova;</item>
/// <item><c>catch</c> do <c>23505</c> retirado →
/// <see cref="AWriteBetweenTheCheckAndTheUpdate_Is409_NotA500"/> reprova em toda execução.</item>
/// </list>
/// </para>
/// </summary>
public class UserEditTests(UserEditTests.Fixture factory, Xunit.Abstractions.ITestOutputHelper output) : IClassFixture<UserEditTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, SignedInUser caller, Guid id, string? name, string? email) =>
        TwoFactorTestClient.SendAsync(client, HttpMethod.Put, $"/users/{id}", caller.AccessToken, new { nome = name, email });

    private Task<HttpResponseMessage> PutAsync(SignedInUser caller, Guid id, string? name, string? email) =>
        PutAsync(SignedInUser.Anonymous(factory), caller, id, name, email);

    [Fact]
    public async Task ChangingTheEmail_MovesTheLogin_AndKeepsTheSub_AndTheSessions()
    {
        var caller = await SignedInUser.CreateAsync(factory, "edita-quem-chama");
        var target = await SignedInUser.CreateAsync(factory, "edita-alvo");
        var newEmail = LocalUserSeed.UniqueEmail("novo");

        var response = await PutAsync(caller, target.User.Id, "  Nome Novo  ", newEmail);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await TwoFactorTestClient.JsonAsync(response);
        Assert.Equal(target.User.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("Nome Novo", body.GetProperty("nome").GetString());
        var client = SignedInUser.Anonymous(factory);
        await LocalUserSeed.AssertPasswordAcceptedAsync(LocalUserSeed.PostSessionAsync(client, newEmail, target.Password), LocalUserSeed.CodeStep);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LocalUserSeed.PostSessionAsync(client, target.User.Email, target.Password)).StatusCode);
        Assert.True(await SignedInUser.RenewsAsync(factory, target.RefreshToken));
    }

    [Fact]
    public async Task SomeoneElsesEmail_InAnotherCase_Is409()
    {
        var caller = await SignedInUser.CreateAsync(factory, "edita-409");
        var owner = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("dono"), SignedInUser.DefaultPassword);
        var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("quer"), SignedInUser.DefaultPassword);

        var response = await PutAsync(caller, target.Id, "Quem Quer", owner.Email.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(target.Email, (await SignedInUser.ReloadAsync(factory.Services, target.Id)).Email);
        Assert.Equal(owner.Email, (await SignedInUser.ReloadAsync(factory.Services, owner.Id)).Email);
    }

    [Fact]
    public async Task OnlyTheCaseOfTheOwnEmail_IsAccepted()
    {
        var caller = await SignedInUser.CreateAsync(factory, "edita-caixa");

        var response = await PutAsync(caller, caller.User.Id, "Pessoa de Teste", caller.User.Email.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("   ", "valido@exemplo.test")]
    [InlineData("Nome", "nao-e-email")]
    [InlineData("Nome", "Fulano <fulano@exemplo.test>")]
    [InlineData(null, "valido@exemplo.test")]
    public async Task InvalidFields_Are400_AndNothingIsWritten(string? name, string email)
    {
        var caller = await SignedInUser.CreateAsync(factory, "edita-400");

        var response = await PutAsync(caller, caller.User.Id, name, email);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(caller.User.Email, (await SignedInUser.ReloadAsync(factory.Services, caller.User.Id)).Email);
    }

    [Fact]
    public async Task UnknownUser_Is404()
    {
        var caller = await SignedInUser.CreateAsync(factory, "edita-404");

        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(caller, Guid.NewGuid(), "Nome", LocalUserSeed.UniqueEmail("x"))).StatusCode);
    }

    [Fact]
    public async Task TwoConcurrentEdits_ToTheSameEmail_AreOneOkAndOne409_NeverA500()
    {
        var caller = await SignedInUser.CreateAsync(factory, "corrida-quem-chama");
        var reproduced = 0;
        for (var round = 0; round < 20; round++)
        {
            var first = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("corrida-a"), SignedInUser.DefaultPassword);
            var second = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("corrida-b"), SignedInUser.DefaultPassword);
            var contested = LocalUserSeed.UniqueEmail("disputado");

            HttpResponseMessage[] responses = [];
            var commands = await factory.SqlCapture.CaptureAsync(async () => responses = await Task.WhenAll(
                PutAsync(caller, first.Id, "A", contested),
                PutAsync(caller, second.Id, "B", contested)));

            var statuses = responses.Select(response => response.StatusCode).Order().ToList();
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statuses);
            reproduced += commands.Any(IsFailedUserUpdate) ? 1 : 0;
        }

        // A corrida até o índice não é garantida: o caso determinístico abaixo é o guarda.
        output.WriteLine($"rodadas em que o 409 veio do índice (UPDATE recusado): {reproduced} de 20");
    }

    [Fact]
    public async Task AWriteBetweenTheCheckAndTheUpdate_Is409_NotA500()
    {
        var caller = await SignedInUser.CreateAsync(factory, "deterministico");
        var target = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("alvo-det"), SignedInUser.DefaultPassword);
        var rival = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("rival-det"), SignedInUser.DefaultPassword);
        var contested = LocalUserSeed.UniqueEmail("disputado-det");
        var interceptor = new RivalWritesFirstInterceptor(target.Id, rival.Id, contested);
        await using var host = WithInterceptor(interceptor);

        IReadOnlyList<string> commands = [];
        HttpResponseMessage response = null!;
        commands = await factory.SqlCapture.CaptureAsync(async () =>
            response = await PutAsync(IdentityIssuerUserFlow.Client(host), caller, target.Id, "Alvo", contested));

        Assert.Equal(1, interceptor.Fired);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(contested, (await SignedInUser.ReloadAsync(factory.Services, rival.Id)).Email);
        Assert.Equal(target.Email, (await SignedInUser.ReloadAsync(factory.Services, target.Id)).Email);
        Assert.Contains(commands, IsFailedUserUpdate);
    }

    private static bool IsFailedUserUpdate(string command) =>
        command.StartsWith("Failed executing DbCommand", StringComparison.Ordinal) && command.Contains("UPDATE users SET", StringComparison.Ordinal);

    private WebApplicationFactory<Program> WithInterceptor(IInterceptor interceptor) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(interceptor))));

    /// <summary>
    /// Na gravação da edição do alvo (a pré-checagem já passou), grava o e-mail disputado no
    /// rival, que é a escrita concorrente que venceu. O comando roda antes da transação do
    /// <c>SaveChanges</c>, em autocommit, e o <c>UPDATE</c> do alvo encontra o índice único
    /// ocupado.
    /// </summary>
    private sealed class RivalWritesFirstInterceptor(Guid targetId, Guid rivalId, string email) : SaveChangesInterceptor
    {
        private int _fired;

        public int Fired => Volatile.Read(ref _fired);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var editsTarget = eventData.Context!.ChangeTracker.Entries<User>()
                .Any(entry => entry.State == EntityState.Modified && entry.Entity.Id == targetId);

            if (editsTarget && Interlocked.Exchange(ref _fired, 1) == 0)
            {
                var normalized = EmailNormalization.Normalize(email);
                await eventData.Context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE users SET email = {email}, normalized_email = {normalized} WHERE id = {rivalId}", cancellationToken);
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
