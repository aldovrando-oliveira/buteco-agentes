using System.Security.Cryptography;
using System.Text;

namespace Buteco.Api.ExternalApplications;

/// <summary>
/// O texto de um segredo de aplicação e o que dele se guarda (design.md da change
/// aplicacoes-externas-credencial, D4).
/// <list type="bullet">
/// <item><b>Texto:</b> <c>btc_sk_</c> mais 32 bytes do gerador criptográfico em base64url, 43
/// caracteres, 256 bits.</item>
/// <item><b>Hash: SHA-256</b>, como o <c>AccessLinkToken</c> e o <c>RecoveryCode</c> da casa. O texto
/// é gerado pelo Buteco com 256 bits, então um hash lento não acrescenta nada contra ataque fora de
/// linha, e o índice único acha o segredo pelo hash sem percorrer os da aplicação. Os clientes de
/// serviço continuam com o PBKDF2-HMAC-SHA256 do OpenIddict (10.000 iterações,
/// <c>OpenIddictApplicationManager.cs</c> l. 1714–1722, tag 7.7.2), pensado para segredo escolhido por
/// gente.</item>
/// <item><b>Sufixo:</b> os 4 últimos caracteres, para a lista; 24 de 256 bits.</item>
/// </list>
/// </summary>
public static class ExternalApplicationSecretValue
{
    public const string Prefix = "btc_sk_";

    public const int RandomBytes = 32;

    public const int SuffixLength = 4;

    public static string Generate() => Prefix + Base64Url(RandomNumberGenerator.GetBytes(RandomBytes));

    /// <summary>
    /// O segredo de enchimento do cliente no OpenIddict (D4): o OpenIddict recusa cliente confidencial
    /// sem segredo (<c>OpenIddictApplicationManager.cs</c> l. 1286–1296), então a linha leva 32 bytes
    /// aleatórios que ninguém guarda. Quem confere o segredo da aplicação é o
    /// <c>ButecoApplicationManager</c>, que nunca aceita este. Sem o gerente (a imagem anterior), ninguém
    /// o conhece, e a aplicação recebe <c>invalid_client</c>: a volta atrás falha fechada.
    /// </summary>
    public static string GenerateFiller() => Base64Url(RandomNumberGenerator.GetBytes(RandomBytes));

    public static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    public static string Suffix(string value) => value[^SuffixLength..];

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
