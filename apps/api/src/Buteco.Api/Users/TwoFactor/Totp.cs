using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Buteco.Api.Users.TwoFactor;

/// <summary>
/// O código de verificação em 2 etapas pelo RFC 6238 (design.md da change
/// verificacao-2-etapas-totp, D3 e D4), sobre o BCL: HMAC-SHA1 do passo em big-endian,
/// truncagem dinâmica do RFC 4226 §5.3 e módulo 10⁶. Constantes, e não opção (convenção 2):
///
/// <list type="bullet">
/// <item><b>6 dígitos, passo de 30 s, T0 = 0</b> (RFC 6238 §5.2), o que todo app autenticador
/// implementa;</item>
/// <item><b>segredo de 20 bytes</b>, o tamanho da saída do HMAC-SHA1 (§5.1);</item>
/// <item><b>janela de um passo para cada lado</b>: §5.2 recomenda no máximo um passo de atraso
/// de rede, e o seguinte cobre o relógio do celular adiantado.</item>
/// </list>
///
/// <para>
/// O reuso (§5.2, "MUST NOT accept the second attempt") não é daqui: quem confere grava o
/// passo devolvido por <see cref="Match"/> e recusa todo passo que não seja maior que o
/// último (<see cref="TwoFactorVerifier"/>).
/// </para>
/// </summary>
public static class Totp
{
    public const int Digits = 6;
    public const int StepSeconds = 30;
    public const int SecretSizeBytes = 20;
    public const int WindowSteps = 1;

    private const int Modulus = 1_000_000;

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretSizeBytes);

    public static long StepAt(DateTimeOffset instant) => instant.ToUnixTimeSeconds() / StepSeconds;

    public static string Compute(ReadOnlySpan<byte> secret, long step)
    {
        Span<byte> counter = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);

        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
        HMACSHA1.HashData(secret, counter, hash);

        var offset = hash[^1] & 0x0F;
        var binary = (BinaryPrimitives.ReadInt32BigEndian(hash.Slice(offset, 4)) & 0x7FFFFFFF) % Modulus;
        return binary.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>O código só com os dígitos, sem os espaços do <c>000 000</c>; nulo se não tiver a forma.</summary>
    public static string? Normalize(string? input)
    {
        if (input is null)
        {
            return null;
        }

        var code = input.Replace(" ", string.Empty, StringComparison.Ordinal);
        return code.Length == Digits && code.All(char.IsAsciiDigit) ? code : null;
    }

    /// <summary>
    /// O passo da janela (atual, anterior, seguinte) cujo código bate, ou nulo. Os três são
    /// calculados e comparados em tempo constante, sem sair no primeiro.
    /// </summary>
    public static long? Match(ReadOnlySpan<byte> secret, string code, DateTimeOffset now)
    {
        var current = StepAt(now);
        var given = Encoding.ASCII.GetBytes(code);
        long? matched = null;

        foreach (var step in (ReadOnlySpan<long>)[current, current - 1, current + 1])
        {
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Compute(secret, step)), given))
            {
                matched ??= step;
            }
        }

        return matched;
    }
}
