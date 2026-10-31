using Buteco.Api.Tests.Support;
using Buteco.Api.Users.Passwords;

namespace Buteco.Api.Tests.Users;

/// <summary>
/// O PBKDF2 de <c>Auth:OperatorPasswordHash</c> só vale com o marcador de legado de
/// bootstrap (spec <c>local-users</c>, "Hash PBKDF2 sem o marcador é recusado"; design.md
/// da change usuarios-locais-login-emissor, D2).
///
/// <para>
/// Verificado contra o defeito (convenção 15): com o <see cref="PasswordHasher.Verify"/>
/// aceitando o formato PBKDF2 sem olhar o marcador, o caso sem marcador reprova (registro
/// no tasks.md, 3.2).
/// </para>
/// </summary>
public class BootstrapLegacyHashTests
{
    private readonly PasswordHasher _hasher = new();
    private readonly string _legacy = TestAuthentication.ComputeKnownOperatorPasswordHash();

    [Fact]
    public void WithTheMarker_TheRightPassword_SucceedsAndAsksForRehash()
    {
        Assert.Equal(
            PasswordVerificationResult.SucceededNeedsRehash,
            _hasher.Verify(TestAuthentication.KnownOperatorPassword, _legacy, isBootstrapLegacy: true));
    }

    [Fact]
    public void WithTheMarker_TheWrongPassword_Fails()
    {
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.Verify("errada", _legacy, isBootstrapLegacy: true));
    }

    [Fact]
    public void WithoutTheMarker_TheSameStringFails()
    {
        Assert.Equal(
            PasswordVerificationResult.Failed,
            _hasher.Verify(TestAuthentication.KnownOperatorPassword, _legacy, isBootstrapLegacy: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("100000.semhash")]
    [InlineData("0.c2FsdA==.aGFzaA==")]
    [InlineData("abc.c2FsdA==.aGFzaA==")]
    [InlineData("100000.!!!.aGFzaA==")]
    public void MalformedLegacyHash_IsNotWellFormed_AndFailsWithoutThrowing(string? stored)
    {
        Assert.False(BootstrapLegacyPasswordVerifier.IsWellFormed(stored));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.Verify("qualquer", stored, isBootstrapLegacy: true));
    }

    [Fact]
    public void TheTestOperatorHash_IsWellFormed()
    {
        Assert.True(BootstrapLegacyPasswordVerifier.IsWellFormed(_legacy));
    }
}
