using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Users;

/// <summary>
/// O limite global do login (spec <c>local-users</c>, "Limite global de verificações de
/// senha"; design.md da change usuarios-locais-login-emissor, D4). Host próprio, com o
/// limite ligado: o estado do limitador é do processo.
/// </summary>
public class LoginRateLimitTests(LoginRateLimitTests.Fixture factory) : IClassFixture<LoginRateLimitTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture
    {
        protected override bool KeepsLoginRateLimit => true;
    }

    [Fact]
    public async Task BurstAboveTheLimit_Gets429WithRetryAfter_AndThoseDoNotVerify()
    {
        // 60 e-mails sem conta ao mesmo tempo: acima da concorrência (4) mais a fila (16),
        // e acima da rajada do token bucket (20). Cada pedido que passa verifica uma senha
        // e grava uma contagem; os recusados não gravam nada.
        var emails = Enumerable.Range(0, 60).Select(_ => LocalUserSeed.UniqueEmail("rajada")).ToList();
        var client = factory.CreateClient(new() { HandleCookies = false });

        var responses = await Task.WhenAll(emails.Select(email => LocalUserSeed.PostSessionAsync(client, email, "x")));

        var limited = responses.Where(response => response.StatusCode == HttpStatusCode.TooManyRequests).ToList();
        Assert.NotEmpty(limited);
        Assert.All(limited, response => Assert.Equal("1", response.Headers.GetValues("Retry-After").Single()));
        Assert.All(responses.Except(limited), response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));

        using var scope = factory.Services.CreateScope();
        var counted = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LoginThrottles
            .CountAsync(row => row.NormalizedEmail.StartsWith("rajada-"));
        Assert.Equal(responses.Length - limited.Count, counted);
    }

    [Fact]
    public async Task OtherRoutes_AreNotLimited()
    {
        var client = factory.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 60).Select(_ => client.GetAsync("/health")));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
    }
}
