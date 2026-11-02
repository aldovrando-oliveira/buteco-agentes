namespace Buteco.Api.Users.TwoFactor;

/// <summary>
/// Base32 do RFC 4648 §6, só a codificação e sem padding: é a forma do segredo no URI
/// <c>otpauth://</c> e no texto que a pessoa digita no app autenticador (D8). O
/// <c>apps/api</c> gera o segredo e nunca o lê de volta nessa forma.
/// </summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var output = new char[(data.Length * 8 + 4) / 5];
        int buffer = 0, bits = 0, index = 0;

        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                output[index++] = Alphabet[(buffer >> (bits - 5)) & 0x1F];
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            output[index] = Alphabet[(buffer << (5 - bits)) & 0x1F];
        }

        return new string(output);
    }
}
