using Buteco.Api.Users.Passwords;

namespace Buteco.Api.Tests.Users;

/// <summary>
/// O Argon2id do <see cref="PasswordHasher"/> (spec <c>local-users</c>, "Senha guardada
/// com Argon2id"; design.md da change usuarios-locais-login-emissor, D4).
/// </summary>
public class PasswordHasherTests
{
    // Da 2.2 do tasks.md: gerado pelo NSec e aceito pelo PasswordHasher().verify do
    // argon2-cffi 25.1.0. Prova que a senha entra em UTF-8 e que o PHC é o da referência.
    private const string ReferenceHash =
        "$argon2id$v=19$m=19456,t=2,p=1$zDMapcVyaZsjkrkalXvucw$q2EOfrQAF9Dwnvxy4cMmdf6sQnbF0y7do6pwslBFeko";

    private const string ReferencePassword = "senha de teste çã";

    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_IsPhcArgon2id_WithThePolicyParameters_AndNotThePassword()
    {
        var hash = _hasher.Hash("uma senha qualquer");

        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", hash);
        Assert.DoesNotContain("uma senha qualquer", hash);
        Assert.Equal(PasswordVerificationResult.Succeeded, _hasher.Verify("uma senha qualquer", hash, isBootstrapLegacy: false));
    }

    [Fact]
    public void Hash_UsesAFreshSaltEachTime()
    {
        Assert.NotEqual(_hasher.Hash("mesma senha"), _hasher.Hash("mesma senha"));
    }

    [Fact]
    public void Verify_AcceptsTheReferenceImplementationVector()
    {
        Assert.Equal(PasswordVerificationResult.Succeeded, _hasher.Verify(ReferencePassword, ReferenceHash, isBootstrapLegacy: false));
    }

    [Fact]
    public void Verify_WrongPassword_Fails()
    {
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.Verify("senha errada", ReferenceHash, isBootstrapLegacy: false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("texto-qualquer")]
    [InlineData("$argon2i$v=19$m=19456,t=2,p=1$zDMapcVyaZsjkrkalXvucw$q2EOfrQAF9Dwnvxy4cMmdf6sQnbF0y7do6pwslBFeko")]
    [InlineData("$argon2id$v=16$m=19456,t=2,p=1$zDMapcVyaZsjkrkalXvucw$q2EOfrQAF9Dwnvxy4cMmdf6sQnbF0y7do6pwslBFeko")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=4$zDMapcVyaZsjkrkalXvucw$q2EOfrQAF9Dwnvxy4cMmdf6sQnbF0y7do6pwslBFeko")]
    [InlineData("$argon2id$v=19$m=4,t=2,p=1$zDMapcVyaZsjkrkalXvucw$q2EOfrQAF9Dwnvxy4cMmdf6sQnbF0y7do6pwslBFeko")]
    [InlineData("$argon2id$v=19$m=4000000000,t=2,p=1$zDMapcVyaZsjkrkalXvucw$q2EOfrQAF9Dwnvxy4cMmdf6sQnbF0y7do6pwslBFeko")]
    [InlineData("$argon2id$v=19$m=19456,t=2$zDMapcVyaZsjkrkalXvucw$q2EOfrQAF9Dwnvxy4cMmdf6sQnbF0y7do6pwslBFeko")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$não-é-base64$q2EOfrQAF9Dwnvxy4cMmdf6sQnbF0y7do6pwslBFeko")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$zDMapcVyaZsjkrkalXvucw")]
    public void Verify_UnknownFormat_FailsWithoutThrowing(string stored)
    {
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.Verify(ReferencePassword, stored, isBootstrapLegacy: false));
    }

    [Fact]
    public void Verify_NullHash_Fails()
    {
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.Verify(ReferencePassword, null, isBootstrapLegacy: false));
    }

    [Fact]
    public void Verify_OlderParameters_SucceedsAndAsksForRehash()
    {
        // t=1 em vez de 2: aceito, e o login refaz o hash com os parâmetros atuais.
        var algorithm = NSec.Cryptography.PasswordBasedKeyDerivationAlgorithm.Argon2id(
            new NSec.Cryptography.Argon2Parameters { DegreeOfParallelism = 1, MemorySize = 19456, NumberOfPasses = 1 });
        var salt = new byte[16];
        var hash = algorithm.DeriveBytes(System.Text.Encoding.UTF8.GetBytes("antiga"), salt, 32);
        var stored = $"$argon2id$v=19$m=19456,t=1,p=1${Convert.ToBase64String(salt).TrimEnd('=')}${Convert.ToBase64String(hash).TrimEnd('=')}";

        Assert.Equal(PasswordVerificationResult.SucceededNeedsRehash, _hasher.Verify("antiga", stored, isBootstrapLegacy: false));
    }

    [Fact]
    public void VerifyAgainstDummy_DoesNotThrow()
    {
        _hasher.VerifyAgainstDummy("qualquer");
    }
}
