using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

namespace Buteco.Api.Users.Passwords;

/// <summary>
/// Argon2id pelo <c>NSec.Cryptography</c> sobre a libsodium (design.md da change
/// usuarios-locais-login-emissor, D4), gravado como string PHC:
/// <c>$argon2id$v=19$m=19456,t=2,p=1$&lt;sal&gt;$&lt;hash&gt;</c>, em base64 sem
/// preenchimento. É o formato da implementação de referência: a 2.2 do tasks.md conferiu
/// a string contra o <c>argon2-cffi</c>.
///
/// <list type="bullet">
/// <item><b>A senha entra em UTF-8.</b> O overload de <c>string</c> do NSec usa os bytes
/// UTF-16 (<c>PasswordBasedKeyDerivationAlgorithm.cs</c> l. 56–66), e o hash não bateria
/// com nenhuma outra implementação;</item>
/// <item><b>formato desconhecido reprova sem exceção</b>, inclusive parâmetros que a
/// libsodium recusa;</item>
/// <item>o PBKDF2 de bootstrap só vale com o marcador
/// (<see cref="BootstrapLegacyPasswordVerifier"/>).</item>
/// </list>
/// </summary>
public sealed class PasswordHasher
{
    private const string Prefix = "$argon2id$";
    private const int Version = 0x13;
    private const long MaxMemorySizeKiB = 256 * 1024;
    private const long MaxNumberOfPasses = 16;

    private static readonly Argon2id CurrentAlgorithm = PasswordBasedKeyDerivationAlgorithm.Argon2id(new Argon2Parameters
    {
        DegreeOfParallelism = PasswordHashPolicy.DegreeOfParallelism,
        MemorySize = PasswordHashPolicy.MemorySizeKiB,
        NumberOfPasses = PasswordHashPolicy.NumberOfPasses,
    });

    // Para o e-mail sem conta: verificar contra ele custa o mesmo que contra uma conta
    // real, e o tempo de resposta não diz se o e-mail existe (D4).
    private static readonly string DummyHash = HashCore("senha-ficticia-para-email-sem-conta");

    public string Hash(string password) => HashCore(password);

    public PasswordVerificationResult Verify(string password, string? storedHash, bool isBootstrapLegacy)
    {
        if (string.IsNullOrEmpty(storedHash))
        {
            return PasswordVerificationResult.Failed;
        }

        if (isBootstrapLegacy)
        {
            return BootstrapLegacyPasswordVerifier.Verify(password, storedHash)
                ? PasswordVerificationResult.SucceededNeedsRehash
                : PasswordVerificationResult.Failed;
        }

        if (!TryParse(storedHash, out var parameters, out var salt, out var expected))
        {
            return PasswordVerificationResult.Failed;
        }

        Argon2id algorithm;
        try
        {
            algorithm = IsCurrent(parameters) ? CurrentAlgorithm : PasswordBasedKeyDerivationAlgorithm.Argon2id(parameters);
        }
        catch (ArgumentException)
        {
            return PasswordVerificationResult.Failed;
        }

        var actual = algorithm.DeriveBytes(Encoding.UTF8.GetBytes(password), salt, expected.Length);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return PasswordVerificationResult.Failed;
        }

        return IsCurrent(parameters) && expected.Length == PasswordHashPolicy.HashSize
            ? PasswordVerificationResult.Succeeded
            : PasswordVerificationResult.SucceededNeedsRehash;
    }

    /// <summary>A verificação do e-mail sem conta: mesmo custo, resultado descartado.</summary>
    public void VerifyAgainstDummy(string password) => Verify(password, DummyHash, isBootstrapLegacy: false);

    private static string HashCore(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(PasswordHashPolicy.SaltSize);
        var hash = CurrentAlgorithm.DeriveBytes(Encoding.UTF8.GetBytes(password), salt, PasswordHashPolicy.HashSize);
        return string.Create(CultureInfo.InvariantCulture,
            $"{Prefix}v={Version}$m={PasswordHashPolicy.MemorySizeKiB},t={PasswordHashPolicy.NumberOfPasses},p={PasswordHashPolicy.DegreeOfParallelism}${Encode(salt)}${Encode(hash)}");
    }

    private static bool IsCurrent(Argon2Parameters parameters) =>
        parameters.MemorySize == PasswordHashPolicy.MemorySizeKiB &&
        parameters.NumberOfPasses == PasswordHashPolicy.NumberOfPasses &&
        parameters.DegreeOfParallelism == PasswordHashPolicy.DegreeOfParallelism;

    private static bool TryParse(string phc, out Argon2Parameters parameters, out byte[] salt, out byte[] hash)
    {
        parameters = default;
        salt = [];
        hash = [];

        // "", "argon2id", "v=19", "m=…,t=…,p=…", sal, hash
        var parts = phc.Split('$');
        if (!phc.StartsWith(Prefix, StringComparison.Ordinal) || parts.Length != 6 || parts[2] != $"v={Version}")
        {
            return false;
        }

        long? memory = null, passes = null;
        int? parallelism = null;
        foreach (var pair in parts[3].Split(','))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length != 2 || !long.TryParse(kv[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            switch (kv[0])
            {
                case "m" when memory is null: memory = value; break;
                case "t" when passes is null: passes = value; break;
                case "p" when parallelism is null && value <= int.MaxValue: parallelism = (int)value; break;
                default: return false;
            }
        }

        // Teto de sanidade: uma linha adulterada com m de gigabytes travaria o processo a
        // cada tentativa. Acima dele, reprova como formato desconhecido.
        if (memory is null || passes is null || parallelism is null ||
            memory > MaxMemorySizeKiB || passes > MaxNumberOfPasses ||
            !TryDecode(parts[4], out salt) || salt.Length != PasswordHashPolicy.SaltSize ||
            !TryDecode(parts[5], out hash) || hash.Length < 16)
        {
            return false;
        }

        parameters = new Argon2Parameters
        {
            MemorySize = memory.Value,
            NumberOfPasses = passes.Value,
            DegreeOfParallelism = parallelism.Value,
        };
        return true;
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static bool TryDecode(string value, out byte[] bytes)
    {
        bytes = [];
        if (value.Length == 0 || value.Contains('='))
        {
            return false;
        }

        var padded = value.PadRight(value.Length + ((4 - (value.Length % 4)) % 4), '=');
        var buffer = new byte[padded.Length];
        if (!Convert.TryFromBase64String(padded, buffer, out var written))
        {
            return false;
        }

        bytes = buffer[..written];
        return true;
    }
}
