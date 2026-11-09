using System.Net;
using Buteco.Api.Identity;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Buteco.Api.Users.AccessLinks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Buteco.Api.Tests.Users.AccessLinks;

/// <summary>
/// Convidar (spec <c>user-access-links</c>, "Convidar cria o usuário no estado convidado" e "O
/// link leva o token no fragmento"; design.md da change convites-redefinicao-senha, D4, D5).
///
/// <para>
/// Verificado contra o defeito (tasks.md, 5.1 e 5.3):
/// <list type="bullet">
/// <item>a base do link lida de <c>Request.Host</c> → <see cref="AForgedHost_DoesNotChangeTheLink"/>
/// reprova;</item>
/// <item><c>catch</c> do <c>23505</c> retirado → <see cref="AnInsertBetweenTheCheckAndTheWrite_Is409_NotA500"/>
/// reprova em toda execução.</item>
/// </list>
/// </para>
/// </summary>
public class InvitationCreateTests(InvitationCreateTests.Fixture factory, ITestOutputHelper output) : IClassFixture<InvitationCreateTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private HttpClient Client => SignedInUser.Anonymous(factory);

    private Uri PanelBase()
    {
        using var scope = factory.Services.CreateScope();
        return PanelBaseUrl.Resolve(
            scope.ServiceProvider.GetRequiredService<IOptions<IdentityIssuerOptions>>().Value,
            scope.ServiceProvider.GetRequiredService<IdentityIssuerState>());
    }

    [Fact]
    public async Task Invite_Returns201_WithTheLinkOnTheConfiguredBase_AndTheTokenInTheFragment()
    {
        var caller = await SignedInUser.CreateAsync(factory, "convida");
        var email = LocalUserSeed.UniqueEmail("convidada");

        var response = await AccessLinkClient.InviteAsync(Client, caller, "  Marcos Lima  ", email, caller.Password);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await AccessLinkClient.JsonAsync(response);
        var link = new Uri(body.GetProperty("link").GetString()!);
        Assert.Equal(new Uri(PanelBase(), "/convite").AbsoluteUri, link.GetLeftPart(UriPartial.Path));
        Assert.Equal("", link.Query);
        Assert.Matches("^#[A-Za-z0-9_-]{43}$", link.Fragment);
        Assert.Equal(factory.Clock.GetUtcNow() + AccessLinkToken.InvitationLifetime, body.GetProperty("expiraEm").GetDateTimeOffset());
        var invited = body.GetProperty("usuario");
        Assert.Equal("convidado", invited.GetProperty("estado").GetString());
        Assert.Equal("Marcos Lima", invited.GetProperty("nome").GetString());
        Assert.Empty(invited.GetProperty("formasDeEntrada").EnumerateArray());

        var stored = await SignedInUser.ReloadAsync(factory.Services, invited.GetProperty("id").GetGuid());
        Assert.Equal(UserStatus.Invited, stored.Status);
        Assert.Null(stored.PasswordHash);
        Assert.Equal(caller.User.Id, stored.InvitedBy);
        Assert.Equal(AccessLinkPurpose.Invitation, stored.AccessLinkPurpose);
        Assert.Equal(AccessLinkToken.Hash(link.Fragment[1..]), stored.AccessLinkHash);
    }

    [Fact]
    public async Task TheToken_IsInNoColumn()
    {
        var caller = await SignedInUser.CreateAsync(factory, "convida-coluna");

        var response = await AccessLinkClient.InviteAsync(Client, caller, "Sem Token", LocalUserSeed.UniqueEmail("sem-token"), caller.Password);
        var token = await AccessLinkClient.TokenAsync(response);
        var id = (await SignedInUser.ReloadAsync(factory.Services, (await SingleInvitedIdAsync(token)))).Id;

        await using var scope = factory.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .SqlQuery<string>($"SELECT row_to_json(u)::text AS \"Value\" FROM users u WHERE u.id = {id}")
            .SingleAsync();
        Assert.DoesNotContain(token, row, StringComparison.Ordinal);
    }

    private async Task<Guid> SingleInvitedIdAsync(string token)
    {
        var hash = AccessLinkToken.Hash(token);
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users
            .Where(user => user.AccessLinkHash == hash).Select(user => user.Id).SingleAsync();
    }

    [Fact]
    public async Task AForgedHost_DoesNotChangeTheLink()
    {
        var caller = await SignedInUser.CreateAsync(factory, "convida-host");
        var request = new HttpRequestMessage(HttpMethod.Post, "/users/invitations")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { nome = "Host", email = LocalUserSeed.UniqueEmail("host"), papel = "admin", senha = caller.Password }),
        };
        request.Headers.Authorization = new("Bearer", caller.AccessToken);
        request.Headers.Host = "evil.example";

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var link = await AccessLinkClient.LinkAsync(response);
        Assert.StartsWith(PanelBase().GetLeftPart(UriPartial.Authority), link, StringComparison.Ordinal);
        Assert.DoesNotContain("evil.example", link, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheEmailOfAnActive_ADeactivated_OrAnInvited_InAnotherCase_Is409()
    {
        var caller = await SignedInUser.CreateAsync(factory, "convida-409");
        var active = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("ativo"), SignedInUser.DefaultPassword);
        var deactivated = await LocalUserSeed.CreateUserAsync(factory.Services, LocalUserSeed.UniqueEmail("desativado"), SignedInUser.DefaultPassword);
        await LocalUserSeed.DeactivateAsync(factory.Services, deactivated.Id);
        var invited = await InvitationSeed.InvitedAsync(factory.Services, caller.User.Id);

        foreach (var email in new[] { active.Email, deactivated.Email, invited.User.Email })
        {
            var response = await AccessLinkClient.InviteAsync(Client, caller, "Repetido", email.ToUpperInvariant(), caller.Password);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync(user => user.Name == "Repetido"));
    }

    [Theory]
    [InlineData("   ", "valido@exemplo.test")]
    [InlineData("Nome", "nao-e-email")]
    public async Task InvalidFields_Are400(string name, string email)
    {
        var caller = await SignedInUser.CreateAsync(factory, "convida-400");

        Assert.Equal(HttpStatusCode.BadRequest, (await AccessLinkClient.InviteAsync(Client, caller, name, email, caller.Password)).StatusCode);
    }

    [Fact]
    public async Task TwoConcurrentInvitations_ForTheSameEmail_AreOne201AndOne409_NeverA500()
    {
        var caller = await SignedInUser.CreateAsync(factory, "corrida-convite");
        var reproduced = 0;
        for (var round = 0; round < 20; round++)
        {
            var contested = LocalUserSeed.UniqueEmail("disputado");

            HttpResponseMessage[] responses = [];
            var commands = await factory.SqlCapture.CaptureAsync(async () => responses = await Task.WhenAll(
                AccessLinkClient.InviteAsync(Client, caller, "A", contested, caller.Password),
                AccessLinkClient.InviteAsync(Client, caller, "B", contested, caller.Password)));

            Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], responses.Select(response => response.StatusCode).Order().ToList());
            reproduced += commands.Any(IsFailedInsert) ? 1 : 0;
        }

        // A corrida até o índice não é garantida: o caso determinístico abaixo é o guarda.
        output.WriteLine($"rodadas em que o 409 veio do índice (INSERT recusado): {reproduced} de 20");
    }

    [Fact]
    public async Task AnInsertBetweenTheCheckAndTheWrite_Is409_NotA500()
    {
        var caller = await SignedInUser.CreateAsync(factory, "deterministico-convite");
        var contested = LocalUserSeed.UniqueEmail("disputado-det");
        var interceptor = new RivalInsertsFirstInterceptor(contested);
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(interceptor))));

        HttpResponseMessage response = null!;
        var commands = await factory.SqlCapture.CaptureAsync(async () =>
            response = await AccessLinkClient.InviteAsync(IdentityIssuerUserFlow.Client(host), caller, "Alvo", contested, caller.Password));

        Assert.Equal(1, interceptor.Fired);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(commands, IsFailedInsert);
    }

    private static bool IsFailedInsert(string command) =>
        command.StartsWith("Failed executing DbCommand", StringComparison.Ordinal) && command.Contains("INSERT INTO users", StringComparison.Ordinal);

    /// <summary>
    /// Na gravação do convite (a pré-checagem já passou), outro usuário com o mesmo e-mail é
    /// gravado em autocommit, e o <c>INSERT</c> do convite encontra o índice único ocupado.
    /// </summary>
    private sealed class RivalInsertsFirstInterceptor(string email) : SaveChangesInterceptor
    {
        private int _fired;

        public int Fired => Volatile.Read(ref _fired);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var normalized = EmailNormalization.Normalize(email);
            var invites = eventData.Context!.ChangeTracker.Entries<User>()
                .Any(entry => entry.State == EntityState.Added && entry.Entity.NormalizedEmail == normalized);

            if (invites && Interlocked.Exchange(ref _fired, 1) == 0)
            {
                var rival = User.Create(email, "Rival", passwordHash: null, DateTimeOffset.UtcNow, UserRole.Admin);
                await eventData.Context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO users (id, email, normalized_email, name, status, security_stamp, created_at, password_hash_is_bootstrap_legacy)
                    VALUES ({rival.Id}, {rival.Email}, {rival.NormalizedEmail}, {rival.Name}, 'active', {rival.SecurityStamp}, {rival.CreatedAt}, false)
                    """,
                    cancellationToken);
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
