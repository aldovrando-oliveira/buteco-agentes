namespace Buteco.Api.Identity.Keys;

/// <summary>
/// Uma chave do emissor OIDC, como fica no banco (design.md da change
/// emissor-oidc-api, D3 e D4). O material privado vai cifrado com
/// <see cref="IdentityKeyMaterialCipher"/>; a linha nunca guarda a chave legível.
///
/// <para>
/// As três janelas vêm de <see cref="IdentityKeyRotationPolicy"/>. Para a chave de
/// cifra, que não roda nesta change, <c>SignsFrom</c> é a criação e
/// <c>SignsUntil</c> e <c>ValidatesUntil</c> ficam no infinito.
/// </para>
/// </summary>
public sealed class IdentityKey
{
    public required string Kid { get; init; }

    public required IdentityKeyUsage Usage { get; init; }

    /// <summary><c>RS256</c> para assinatura, <c>A256KW</c> para cifra.</summary>
    public required string Algorithm { get; init; }

    /// <summary><c>nonce || ciphertext || tag</c> do PKCS#8 (RSA) ou dos 32 bytes (AES).</summary>
    public required byte[] ProtectedMaterial { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset SignsFrom { get; init; }

    public required DateTimeOffset SignsUntil { get; init; }

    public required DateTimeOffset ValidatesUntil { get; init; }
}
