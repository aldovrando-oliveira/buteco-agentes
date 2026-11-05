using Buteco.Api.Identity;
using Buteco.Api.Identity.Keys;
using Buteco.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Server;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// As quatro durações de token do emissor saem de <see cref="IdentityTokenLifetimes"/> (spec
/// <c>identity-issuer-overview</c>, "As durações saem das mesmas constantes que o emissor usa";
/// design.md da change leitura-identidade-emissor, D6). As opções lidas são as que o OpenIddict
/// de fato usa, compostas no host, e não as constantes de novo.
/// </summary>
public class IdentityTokenLifetimesTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private async Task<OpenIddictServerOptions> ComposedAsync()
    {
        // Sem o anel, a composição das opções lança (emissor-oidc-api, D6).
        await factory.Services.GetRequiredService<IdentityKeyRing>().EnsureLoadedAsync(CancellationToken.None);
        return factory.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
    }

    [Fact]
    public async Task AccessToken_IsTheConstant()
    {
        Assert.Equal(IdentityTokenLifetimes.AccessToken, (await ComposedAsync()).AccessTokenLifetime);
    }

    [Fact]
    public async Task RefreshToken_IsTheConstant()
    {
        Assert.Equal(IdentityTokenLifetimes.RefreshToken, (await ComposedAsync()).RefreshTokenLifetime);
    }

    [Fact]
    public async Task IdentityToken_IsTheConstant()
    {
        Assert.Equal(IdentityTokenLifetimes.IdentityToken, (await ComposedAsync()).IdentityTokenLifetime);
    }

    [Fact]
    public async Task AuthorizationCode_IsTheConstant()
    {
        Assert.Equal(IdentityTokenLifetimes.AuthorizationCode, (await ComposedAsync()).AuthorizationCodeLifetime);
    }
}
