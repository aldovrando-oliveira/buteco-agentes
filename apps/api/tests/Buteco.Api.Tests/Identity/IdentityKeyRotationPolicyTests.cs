using Buteco.Api.Identity.Keys;
using Microsoft.Extensions.Time.Testing;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// As janelas da rotação (design.md da change emissor-oidc-api, D7), andando com o
/// relógio: primeira chave, publicação antecipada a 7 dias, troca, e saída do anel uma
/// retenção (15 dias, change usuarios-locais-login-emissor D3.7) depois de parar de assinar.
/// </summary>
public class IdentityKeyRotationPolicyTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void AdvisoryLockKey_IsBUTECOK1_ReadAsABigEndianInteger()
    {
        var bytes = BitConverter.GetBytes(IdentityKeyRotationPolicy.AdvisoryLockKey);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        Assert.Equal("BUTECOK1", System.Text.Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void EmptyTable_PlansOneSigningKeyStartingNow_AndOneEncryptionKey()
    {
        var now = _clock.GetUtcNow();

        var plan = IdentityKeyRotationPolicy.Plan([], now);

        Assert.True(plan.NeedsEncryptionKey);
        Assert.Equal(now, plan.NewSigningKey!.SignsFrom);
        Assert.Equal(now.AddDays(90), plan.NewSigningKey.SignsUntil);
        Assert.Equal(now.AddDays(90).AddDays(15), plan.NewSigningKey.ValidatesUntil);
    }

    [Fact]
    public void ActiveKey_FarFromTheEnd_PlansNothing()
    {
        var keys = Initial(_clock.GetUtcNow());

        _clock.Advance(TimeSpan.FromDays(83) - TimeSpan.FromMinutes(1));

        Assert.True(IdentityKeyRotationPolicy.Plan(keys, _clock.GetUtcNow()).IsEmpty);
    }

    [Fact]
    public void LessThanSevenDaysLeft_PlansTheNextKey_StartingWhenTheActiveStops()
    {
        var keys = Initial(_clock.GetUtcNow());
        var active = keys.Single(key => key.Usage == IdentityKeyUsage.Signing);

        _clock.Advance(TimeSpan.FromDays(83) + TimeSpan.FromMinutes(1));
        var plan = IdentityKeyRotationPolicy.Plan(keys, _clock.GetUtcNow());

        Assert.False(plan.NeedsEncryptionKey);
        Assert.Equal(active.SignsUntil, plan.NewSigningKey!.SignsFrom);
        Assert.Equal(active.SignsUntil.AddDays(90), plan.NewSigningKey.SignsUntil);

        // Publicada já (está no anel), sem assinar ainda.
        var next = Row(plan.NewSigningKey);
        Assert.True(IdentityKeyRotationPolicy.IsInRing(next, _clock.GetUtcNow()));
        Assert.False(IdentityKeyRotationPolicy.SignsAt(next, _clock.GetUtcNow()));
        Assert.True(IdentityKeyRotationPolicy.SignsAt(active, _clock.GetUtcNow()));
    }

    [Fact]
    public void NextKeyAlreadyPublished_PlansNothingMore()
    {
        var keys = Initial(_clock.GetUtcNow());
        _clock.Advance(TimeSpan.FromDays(84));
        keys.Add(Row(IdentityKeyRotationPolicy.Plan(keys, _clock.GetUtcNow()).NewSigningKey!));

        _clock.Advance(TimeSpan.FromHours(1));

        Assert.True(IdentityKeyRotationPolicy.Plan(keys, _clock.GetUtcNow()).IsEmpty);
    }

    [Fact]
    public void AfterTheSwitch_TheNewKeySigns_TheOldOneStaysInTheRingForTheRetention()
    {
        var keys = Initial(_clock.GetUtcNow());
        var old = keys.Single(key => key.Usage == IdentityKeyUsage.Signing);
        _clock.Advance(TimeSpan.FromDays(84));
        var next = Row(IdentityKeyRotationPolicy.Plan(keys, _clock.GetUtcNow()).NewSigningKey!);
        keys.Add(next);

        _clock.SetUtcNow(old.SignsUntil);
        Assert.True(IdentityKeyRotationPolicy.SignsAt(next, _clock.GetUtcNow()));
        Assert.False(IdentityKeyRotationPolicy.SignsAt(old, _clock.GetUtcNow()));
        Assert.True(IdentityKeyRotationPolicy.IsInRing(old, _clock.GetUtcNow()));

        _clock.Advance(IdentityKeyRotationPolicy.Retention - TimeSpan.FromSeconds(1));
        Assert.True(IdentityKeyRotationPolicy.IsInRing(old, _clock.GetUtcNow()));

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(IdentityKeyRotationPolicy.IsInRing(old, _clock.GetUtcNow()));
    }

    [Fact]
    public void Retention_CoversTheFourteenDayRefreshPlusTheCheckInterval()
    {
        Assert.Equal(TimeSpan.FromDays(15), IdentityKeyRotationPolicy.Retention);
        Assert.True(IdentityKeyRotationPolicy.Retention >= TimeSpan.FromDays(14) + IdentityKeyRotationPolicy.CheckInterval);
    }

    [Fact]
    public void EveryKeyExpired_PlansASigningKeyStartingNow()
    {
        var keys = Initial(_clock.GetUtcNow());

        _clock.Advance(TimeSpan.FromDays(200));
        var plan = IdentityKeyRotationPolicy.Plan(keys, _clock.GetUtcNow());

        Assert.Equal(_clock.GetUtcNow(), plan.NewSigningKey!.SignsFrom);
        Assert.False(plan.NeedsEncryptionKey);
    }

    // GET /identity (spec identity-issuer-overview, design.md da change
    // leitura-identidade-emissor, D3): estado de cada chave pela mesma conta da rotação.

    [Fact]
    public void Describe_ListsInUseNextAndRetiring_InTheOrderOfTheIdentityBoard()
    {
        var now = _clock.GetUtcNow();
        var retiring = Row(new IdentityKeyWindows(now.AddDays(-100), now.AddDays(-10), now.AddDays(5)));
        var inUse = Row(new IdentityKeyWindows(now.AddDays(-10), now.AddDays(80), now.AddDays(95)));
        var next = Row(new IdentityKeyWindows(now.AddDays(80), now.AddDays(170), now.AddDays(185)));

        var described = IdentityKeyRotationPolicy.Describe([View(next), View(retiring), View(inUse)], now);

        Assert.Equal(
            [(inUse.Kid, IdentityKeyState.InUse), (next.Kid, IdentityKeyState.Next), (retiring.Kid, IdentityKeyState.Retiring)],
            described.Select(d => (d.Key.Kid, d.State)));
    }

    [Fact]
    public void Describe_AtTheExactBorders_FollowsTheRingOperators()
    {
        var now = _clock.GetUtcNow();
        var startsNow = Row(new IdentityKeyWindows(now, now.AddDays(90), now.AddDays(105)));
        var stopsNow = Row(new IdentityKeyWindows(now.AddDays(-90), now, now.AddDays(15)));
        var leavesNow = Row(new IdentityKeyWindows(now.AddDays(-105), now.AddDays(-15), now));

        var described = IdentityKeyRotationPolicy.Describe([View(startsNow), View(stopsNow), View(leavesNow)], now);

        Assert.Equal(
            [(startsNow.Kid, IdentityKeyState.InUse), (stopsNow.Kid, IdentityKeyState.Retiring)],
            described.Select(d => (d.Key.Kid, d.State)));
    }

    [Fact]
    public void Describe_LeavesOutTheKeyThatNoLongerValidates()
    {
        var now = _clock.GetUtcNow();
        var inUse = Row(new IdentityKeyWindows(now.AddDays(-1), now.AddDays(89), now.AddDays(104)));
        var expired = Row(new IdentityKeyWindows(now.AddDays(-200), now.AddDays(-110), now.AddDays(-1)));

        var described = IdentityKeyRotationPolicy.Describe([View(inUse), View(expired)], now);

        Assert.Equal([inUse.Kid], described.Select(d => d.Key.Kid));
    }

    [Fact]
    public void Describe_InUse_IsTheKeyTheRingSnapshotSignsWith()
    {
        // Duas chaves assinando ao mesmo tempo não acontecem com as janelas contíguas, mas a
        // regra de desempate tem que ser a do snapshot, e é aqui que ela aparece.
        var now = _clock.GetUtcNow();
        var cipher = new IdentityKeyMaterialCipher(Microsoft.Extensions.Options.Options.Create(
            new Buteco.Api.Identity.IdentityIssuerOptions { SigningKeyEncryptionKey = Support.TestAuthentication.IdentitySigningKeyEncryptionKey }));
        var factory = new IdentityKeyFactory(cipher);
        var older = factory.CreateSigningKey(new IdentityKeyWindows(now.AddDays(-30), now.AddDays(60), now.AddDays(75)), now.AddDays(-37));
        var newer = factory.CreateSigningKey(new IdentityKeyWindows(now.AddDays(-5), now.AddDays(85), now.AddDays(100)), now.AddDays(-12));
        var encryption = factory.CreateEncryptionKey(IdentityKeyRotationPolicy.EncryptionKey(now.AddDays(-37)), now.AddDays(-37));

        var signingKid = IdentityKeyRingSnapshot.Build([older, newer, encryption], now, cipher).SigningKid;
        var described = IdentityKeyRotationPolicy.Describe([View(older), View(newer)], now);

        Assert.Equal(signingKid, described.Single(d => d.State == IdentityKeyState.InUse).Key.Kid);
        Assert.Equal(newer.Kid, signingKid);
    }

    [Fact]
    public void Plan_CreatesTheNextKey_ExactlyAtThePrePublicationThreshold()
    {
        // A pré-publicação que o GET /identity informa é o limiar que o Plan usa.
        var now = _clock.GetUtcNow();
        var justInside = Row(new IdentityKeyWindows(now.AddDays(-80), now + IdentityKeyRotationPolicy.PrePublication - TimeSpan.FromSeconds(1), now.AddDays(30)));
        var justOutside = Row(new IdentityKeyWindows(now.AddDays(-80), now + IdentityKeyRotationPolicy.PrePublication + TimeSpan.FromSeconds(1), now.AddDays(30)));
        var encryption = Row(IdentityKeyRotationPolicy.EncryptionKey(now), IdentityKeyUsage.Encryption);

        Assert.NotNull(IdentityKeyRotationPolicy.Plan([justInside, encryption], now).NewSigningKey);
        Assert.Null(IdentityKeyRotationPolicy.Plan([justOutside, encryption], now).NewSigningKey);
    }

    private static IdentityKeyWindowsView View(IdentityKey key) =>
        new(key.Kid, key.CreatedAt, key.SignsFrom, key.SignsUntil, key.ValidatesUntil);

    private static List<IdentityKey> Initial(DateTimeOffset now) =>
    [
        Row(IdentityKeyRotationPolicy.FirstSigningKey(now)),
        Row(IdentityKeyRotationPolicy.EncryptionKey(now), IdentityKeyUsage.Encryption),
    ];

    private static IdentityKey Row(IdentityKeyWindows windows, IdentityKeyUsage usage = IdentityKeyUsage.Signing) => new()
    {
        Kid = Guid.NewGuid().ToString("N"),
        Usage = usage,
        Algorithm = usage == IdentityKeyUsage.Signing ? "RS256" : "A256KW",
        ProtectedMaterial = [],
        CreatedAt = windows.SignsFrom,
        SignsFrom = windows.SignsFrom,
        SignsUntil = windows.SignsUntil,
        ValidatesUntil = windows.ValidatesUntil,
    };
}
