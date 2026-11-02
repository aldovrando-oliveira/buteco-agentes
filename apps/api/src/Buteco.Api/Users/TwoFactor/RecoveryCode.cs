using System.Security.Cryptography;
using System.Text;

namespace Buteco.Api.Users.TwoFactor;

/// <summary>
/// Os códigos de recuperação (design.md da change verificacao-2-etapas-totp, D6): 10 por
/// usuário, de 16 caracteres do alfabeto de Crockford (80 bits), mostrados como
/// <c>XXXX-XXXX-XXXX-XXXX</c> e guardados só como SHA-256 do normalizado.
///
/// <para>
/// <b>SHA-256, e não Argon2id:</b> com 80 bits aleatórios, a força bruta fora do sistema,
/// com uma cópia do banco, não alcança o código, e o Argon2id custaria ~19 MiB por
/// tentativa sem saber qual dos 10 conferir. A normalização perdoa o que se erra ao
/// digitar: caixa, hífens, espaços, e as trocas de Crockford (<c>I</c>/<c>L</c> → <c>1</c>,
/// <c>O</c> → <c>0</c>).
/// </para>
/// </summary>
public static class RecoveryCode
{
    public const int Count = 10;
    public const int Length = 16;

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Um código novo, já na forma mostrada.</summary>
    public static string Generate()
    {
        var symbols = RandomNumberGenerator.GetItems<char>(Alphabet, Length);
        return string.Join('-', Enumerable.Range(0, Length / 4).Select(group => new string(symbols, group * 4, 4)));
    }

    /// <summary>Os 10 de uma geração, distintos.</summary>
    public static IReadOnlyList<string> GenerateSet()
    {
        var codes = new HashSet<string>();
        while (codes.Count < Count)
        {
            codes.Add(Generate());
        }

        return [.. codes];
    }

    /// <summary>O código normalizado, ou nulo se não tiver a forma de um código de recuperação.</summary>
    public static string? Normalize(string? input)
    {
        if (input is null)
        {
            return null;
        }

        var builder = new StringBuilder(Length);
        foreach (var raw in input)
        {
            if (raw is '-' or ' ')
            {
                continue;
            }

            var symbol = char.ToUpperInvariant(raw) switch
            {
                'I' or 'L' => '1',
                'O' => '0',
                var other => other,
            };

            if (!Alphabet.Contains(symbol, StringComparison.Ordinal))
            {
                return null;
            }

            builder.Append(symbol);
        }

        return builder.Length == Length ? builder.ToString() : null;
    }

    public static byte[] Hash(string normalized) => SHA256.HashData(Encoding.ASCII.GetBytes(normalized));
}
