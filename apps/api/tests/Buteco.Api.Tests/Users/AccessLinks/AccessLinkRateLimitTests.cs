using System.Net;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Users.AccessLinks;

/// <summary>
/// O aceite e a conclusão entram no limite global de verificações de senha; as consultas não
/// (spec <c>user-access-links</c>; design.md da change convites-redefinicao-senha, D8). Host
/// próprio, com o limite ligado, como o <c>LoginRateLimitTests</c>: o estado do limitador é do
/// processo.
///
/// <para>
/// Verificado contra o defeito (tasks.md, 6.8): o <c>LoginRateLimiting.Marker</c> retirado do
/// aceite → <see cref="ABurstOfAccepts_GetsSome429_AndABurstOfLookups_None"/> reprova.
/// </para>
/// </summary>
public class AccessLinkRateLimitTests(AccessLinkRateLimitTests.Fixture factory) : IClassFixture<AccessLinkRateLimitTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture
    {
        protected override bool KeepsLoginRateLimit => true;
    }

    [Fact]
    public async Task ABurstOfAccepts_GetsSome429_AndABurstOfLookups_None()
    {
        // Tokens inexistentes: o limitador age antes do endpoint, e nenhuma senha é calculada.
        var client = factory.CreateClient(new() { HandleCookies = false });

        var accepts = await Task.WhenAll(Enumerable.Range(0, 60).Select(_ =>
            AccessLinkClient.AcceptAsync(client, Guid.NewGuid().ToString("N"), "uma-senha-dentro-da-politica")));
        await Task.Delay(TimeSpan.FromSeconds(3));
        var lookups = await Task.WhenAll(Enumerable.Range(0, 60).Select(_ =>
            AccessLinkClient.LookupInvitationAsync(client, Guid.NewGuid().ToString("N"))));

        var limited = accepts.Where(response => response.StatusCode == HttpStatusCode.TooManyRequests).ToList();
        Assert.NotEmpty(limited);
        Assert.All(limited, response => Assert.Equal("1", response.Headers.GetValues("Retry-After").Single()));
        Assert.All(accepts.Except(limited), response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.All(lookups, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
    }
}
