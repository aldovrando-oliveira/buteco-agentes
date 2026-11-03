using System.Collections.Concurrent;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Buteco.Api.Tests.Users.AccessLinks;

/// <summary>
/// O token fora dos logs (spec <c>user-access-links</c>, "O link leva o token no fragmento";
/// design.md da change convites-redefinicao-senha, D3). O host sobe o log de hosting do ASP.NET
/// Core para <c>Information</c>, o nível em que ele registra caminho e query de toda requisição
/// (tasks.md, 2.2), e captura todas as categorias.
///
/// <para>
/// Verificado contra o defeito (tasks.md, 6.9), um de cada vez: um log do aceite que leva o token
/// → <see cref="AWholeCycle_LeavesTheTokenInNoLogLine"/> reprova; a consulta do convite também por
/// <c>GET /auth/invitation/{token}</c> → <see cref="NoRoute_TakesTheTokenInThePathOrQuery"/> reprova.
/// </para>
/// </summary>
public class AccessLinkLogTests(AccessLinkLogTests.Fixture factory) : IClassFixture<AccessLinkLogTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureLogging(logging =>
            {
                logging.AddProvider(new Capture(Lines));
                logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Information);
            });
        }

        private sealed class Capture(ConcurrentQueue<string> lines) : ILoggerProvider
        {
            public ILogger CreateLogger(string categoryName) => new Sink(categoryName, lines);

            public void Dispose()
            {
            }
        }

        private sealed class Sink(string category, ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue($"{category}|{formatter(state, exception)}|{exception}");
        }
    }

    [Fact]
    public async Task AWholeCycle_LeavesTheTokenInNoLogLine()
    {
        var client = SignedInUser.Anonymous(factory);
        var caller = await SignedInUser.CreateAsync(factory, "log-convida");
        var target = await SignedInUser.CreateAsync(factory, "log-alvo");

        var invitationToken = await AccessLinkClient.TokenAsync(
            await AccessLinkClient.InviteAsync(client, caller, "Pelo Log", LocalUserSeed.UniqueEmail("log"), caller.Password));
        await AccessLinkClient.LookupInvitationAsync(client, invitationToken);
        await AccessLinkClient.AcceptAsync(client, invitationToken, "senha-nova-pelo-log");
        var resetToken = await AccessLinkClient.TokenAsync(await AccessLinkClient.ResetAsync(client, caller, target.User.Id, caller.Password));
        await AccessLinkClient.LookupResetAsync(client, resetToken);
        await AccessLinkClient.CompleteAsync(client, resetToken, "outra-senha-pelo-log");

        Assert.Contains(factory.Lines, line => line.Contains("Request starting", StringComparison.Ordinal) && line.Contains("/auth/invitation", StringComparison.Ordinal));
        foreach (var token in new[] { invitationToken, resetToken })
        {
            Assert.DoesNotContain(factory.Lines, line => line.Contains(token, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task NoRoute_TakesTheTokenInThePathOrQuery()
    {
        var caller = await SignedInUser.CreateAsync(factory, "log-caminho");
        var client = SignedInUser.Anonymous(factory);
        var token = await AccessLinkClient.TokenAsync(
            await AccessLinkClient.InviteAsync(client, caller, "Pelo Caminho", LocalUserSeed.UniqueEmail("caminho"), caller.Password));

        foreach (var path in new[] { $"/auth/invitation/{token}", $"/auth/invitation?token={token}", $"/auth/password-reset/{token}" })
        {
            var response = await client.GetAsync(path);
            Assert.True(response.StatusCode != System.Net.HttpStatusCode.OK, $"GET {path} → {(int)response.StatusCode}");
        }
    }
}
