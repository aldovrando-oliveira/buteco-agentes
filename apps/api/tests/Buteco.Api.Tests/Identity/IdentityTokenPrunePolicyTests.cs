using Buteco.Api.Identity.Keys;
using Buteco.Api.Identity.Storage;
using OpenIddict.Server;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// As constantes da limpeza (design.md da change limpeza-tokens-openiddict, D3 e D5): a
/// chave do lock é própria, e a idade mínima cobre a duração padrão do refresh token.
/// </summary>
public class IdentityTokenPrunePolicyTests
{
    [Fact]
    public void AdvisoryLockKey_IsBUTECOP1_ReadAsABigEndianInteger()
    {
        var bytes = BitConverter.GetBytes(IdentityTokenPrunePolicy.AdvisoryLockKey);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        Assert.Equal("BUTECOP1", System.Text.Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void AdvisoryLockKey_DiffersFromTheRotationKey()
    {
        Assert.NotEqual(IdentityKeyRotationPolicy.AdvisoryLockKey, IdentityTokenPrunePolicy.AdvisoryLockKey);
    }

    [Fact]
    public void MinimumAge_CoversTheDefaultRefreshTokenLifetime()
    {
        var defaultLifetime = new OpenIddictServerOptions().RefreshTokenLifetime;

        Assert.NotNull(defaultLifetime);
        Assert.True(
            IdentityTokenPrunePolicy.MinimumAge >= defaultLifetime,
            $"idade mínima {IdentityTokenPrunePolicy.MinimumAge} abaixo do refresh token padrão {defaultLifetime}");
    }
}
