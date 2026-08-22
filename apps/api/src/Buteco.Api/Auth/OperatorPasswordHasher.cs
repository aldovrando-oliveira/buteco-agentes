using System.Security.Cryptography;
using System.Text;

namespace Buteco.Api.Auth;

// Verificação via Rfc2898DeriveBytes.Pbkdf2 (BCL), sem pacote novo —
// mesmo padrão de "criptografia crua do BCL" de AesGcmChannelCredentialCipher
// (apps/inbox) e do próprio TokenService (design.md, Decision 5). O hash
// armazenado em Auth:OperatorPasswordHash é gerado offline, fora deste
// código: "{iterations}.{saltBase64}.{hashBase64}".
public static class OperatorPasswordHasher
{
    public static bool Verify(string password, string storedHash)
    {
        var parts = storedHash.Split('.', 3);
        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expectedHash = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var computedHash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return computedHash.Length == expectedHash.Length &&
            CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
    }
}
