using Buteco.Api.Identity.Keys;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// Um refresh emitido pela chave anterior renova 13 dias depois da troca (spec
/// <c>identity-signing-keys</c>, "Refresh emitido pela chave anterior renova 13 dias depois
/// da troca"; design.md da change usuarios-locais-login-emissor, D3.7). O OpenIddict valida
/// o refresh com as chaves do anel, e a retenção precisa cobrir os 14 dias dele.
///
/// <para>
/// O relógio do host é o <see cref="LocalUsersFixture.Clock"/>: ele governa a criação e a
/// troca das chaves, e também a validade dos tokens no OpenIddict, que confere a expiração
/// pelo <c>TimeProvider</c> e não pelo IdentityModel. O teste roda com a duração real do
/// refresh, sem ajuste.
/// </para>
/// <para>
/// Verificado contra o defeito (convenção 15): com a retenção de volta a 24 h, o refresh é
/// recusado e o teste reprova (registro no tasks.md, 6.9).
/// </para>
/// </summary>
public class IdentityKeyRetentionRefreshTests(LocalUsersFixture factory) : IClassFixture<LocalUsersFixture>
{
    private const string Password = "senha-certa-do-teste";

    [Fact]
    public async Task ARefreshSignedByThePreviousKey_RenewsThirteenDaysAfterTheSwitch()
    {
        var ring = factory.Services.GetRequiredService<IdentityKeyRing>();
        var client = IdentityIssuerUserFlow.Client(factory);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        var firstKid = ring.Snapshot!.SigningKid;
        IdentityKey firstKey;
        using (var scope = factory.Services.CreateScope())
        {
            firstKey = await scope.ServiceProvider.GetRequiredService<AppDbContext>().IdentityKeys.AsNoTracking().SingleAsync(key => key.Kid == firstKid);
        }

        // Uma hora antes da troca: o refresh sai assinado pela chave que vai parar.
        factory.Clock.SetUtcNow(firstKey.SignsUntil - TimeSpan.FromHours(1));
        await ring.RefreshAsync(CancellationToken.None);
        var email = LocalUserSeed.UniqueEmail("retencao");
        await LocalUserSeed.CreateUserAsync(factory.Services, email, Password);
        var tokens = await IdentityIssuerUserFlow.SignInAsync(client, factory.Services, email, Password);
        Assert.Equal(firstKid, new JsonWebToken(tokens.AccessToken).Kid);

        // A troca, e 13 dias depois.
        factory.Clock.SetUtcNow(firstKey.SignsUntil + TimeSpan.FromMinutes(1));
        await ring.RefreshAsync(CancellationToken.None);
        factory.Clock.Advance(TimeSpan.FromDays(13));
        await ring.RefreshAsync(CancellationToken.None);

        var renewed = await IdentityIssuerUserFlow.ReadTokensAsync(await IdentityIssuerUserFlow.RefreshAsync(client, tokens.RefreshToken));

        Assert.NotEqual(firstKid, new JsonWebToken(renewed.AccessToken).Kid);
    }
}
