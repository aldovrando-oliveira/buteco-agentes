using System.Security.Cryptography;
using System.Text;
using Buteco.Api.ExternalApplications;

namespace Buteco.Api.Tests.ExternalApplications;

/// <summary>
/// O segredo e o client ID sem banco (change aplicacoes-externas-credencial, D3 e D4; spec
/// <c>external-applications</c>): o formato, o hash, o sufixo, a sobreposição e o estado derivado.
/// </summary>
public class ExternalApplicationSecretTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheValue_IsBtcSkPlus43Base64UrlCharacters_AndOnlyTheHashAndSuffixAreKept()
    {
        var value = ExternalApplicationSecretValue.Generate();
        var secret = ExternalApplicationSecret.Create(Guid.NewGuid(), value, Now);

        Assert.Matches("^btc_sk_[A-Za-z0-9_-]{43}$", value);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(value)), secret.Hash);
        Assert.Equal(value[^4..], secret.Suffix);
        Assert.Equal(Now + TimeSpan.FromDays(365), secret.ExpiresAt);
    }

    [Fact]
    public void TheClientId_IsBtcAppPlus8_FromTheAlphabetWithoutLookalikes()
    {
        var generator = new ExternalApplicationClientId();
        var ids = Enumerable.Range(0, 200).Select(_ => generator.Next()).ToList();

        Assert.All(ids, id => Assert.Matches("^btc_app_[23456789abcdefghijkmnpqrstuvwxyz]{8}$", id));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Theory]
    [InlineData("buteco-x")]
    [InlineData("BUTECO-painel")]
    public void Ensure_RefusesTheReservedPrefix(string clientId) =>
        Assert.Throws<InvalidOperationException>(() => ExternalApplicationClientId.Ensure(clientId));

    // "Os quatro estados" (spec external-applications), na ordem da spec: revogado, depois expirado,
    // depois substituído, depois ativo.
    [Fact]
    public void TheState_IsRevokedThenExpiredThenSupersededThenActive()
    {
        var revoked = ExternalApplicationSecret.Create(Guid.NewGuid(), ExternalApplicationSecretValue.Generate(), Now - TimeSpan.FromDays(400));
        revoked.Revoke(Now - TimeSpan.FromDays(399));
        var expired = ExternalApplicationSecret.Create(Guid.NewGuid(), ExternalApplicationSecretValue.Generate(), Now - TimeSpan.FromDays(366));
        var superseded = ExternalApplicationSecret.Create(Guid.NewGuid(), ExternalApplicationSecretValue.Generate(), Now - TimeSpan.FromDays(10));
        var newest = ExternalApplicationSecret.Create(Guid.NewGuid(), ExternalApplicationSecretValue.Generate(), Now);

        Assert.Equal(ExternalApplicationSecretState.Revoked, revoked.StateAt(Now, hasNewer: true));
        Assert.Equal(ExternalApplicationSecretState.Expired, expired.StateAt(Now, hasNewer: true));
        Assert.Equal(ExternalApplicationSecretState.Superseded, superseded.StateAt(Now, hasNewer: true));
        Assert.Equal(ExternalApplicationSecretState.Active, newest.StateAt(Now, hasNewer: false));
        Assert.False(revoked.IsAliveAt(Now));
        Assert.False(expired.IsAliveAt(Now));
        Assert.True(superseded.IsAliveAt(Now));
    }

    [Fact]
    public void Supersede_LimitsTo7Days_AndNeverExtends()
    {
        var longLived = ExternalApplicationSecret.Create(Guid.NewGuid(), ExternalApplicationSecretValue.Generate(), Now);
        var aboutToExpire = ExternalApplicationSecret.Create(Guid.NewGuid(), ExternalApplicationSecretValue.Generate(), Now - TimeSpan.FromDays(363));

        longLived.Supersede(Now);
        aboutToExpire.Supersede(Now);

        Assert.Equal(Now + TimeSpan.FromDays(7), longLived.ExpiresAt);
        Assert.Equal(Now + TimeSpan.FromDays(2), aboutToExpire.ExpiresAt);
    }

    [Fact]
    public void RevokingTwice_KeepsTheFirstDate()
    {
        var secret = ExternalApplicationSecret.Create(Guid.NewGuid(), ExternalApplicationSecretValue.Generate(), Now);

        secret.Revoke(Now);
        secret.Revoke(Now + TimeSpan.FromHours(1));

        Assert.Equal(Now, secret.RevokedAt);
    }
}
