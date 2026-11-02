using System.Security.Cryptography;
using System.Text;
using Buteco.Api.Infrastructure;

namespace Buteco.Api.Tests.Infrastructure;

/// <summary>
/// O envelope AES-GCM extraído no terceiro consumidor (design.md da change
/// verificacao-2-etapas-totp, D5). Os vetores foram cifrados pelo código de antes da extração
/// (<c>86abb5d</c>), com as chaves de desenvolvimento, e o de MCP foi decifrado também pela
/// cópia do <c>apps/workers</c> (tasks.md, 2.1): abrir os dois aqui prova que o formato
/// gravado não mudou.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 4.1: com o formato lido
/// com a tag antes do texto, os dois vetores reprovam.
/// </para>
/// </summary>
public class AesGcmEnvelopeTests
{
    private const string McpKey = "NtxqjqnKG3sqy52PFRh/SGk573bsE9TrDtKOsDiR8uc=";
    private const string McpStored = "dapDH6JS17MTdL2BWxIyaH9nmjuc4mg/31J6Q8af4BYhryXGEveJ6a+5NLP7wi/a";
    private const string IdentityKey = "zQlhPZ0Htdzc8d1nYvrQuOmtevxRjlta5lNKXN8l+E0=";
    private const string IdentityStored = "gZF1xyhRIjxvEWZEfO93FVuH3oOzLAOt3T0Ew3/lIaUjCYh0KKWhxcWq7EHCF1imBg==";

    private static byte[] Key(string base64) => AesGcmEnvelope.ParseKey("Teste:Chave", base64, "sem chave");

    [Fact]
    public void McpVector_FromBeforeTheExtraction_Opens()
    {
        var plaintext = AesGcmEnvelope.Open(Key(McpKey), Convert.FromBase64String(McpStored), "curto");

        Assert.Equal("segredo-de-teste-218", Encoding.UTF8.GetString(plaintext));
    }

    [Fact]
    public void IdentityKeyVector_FromBeforeTheExtraction_Opens()
    {
        var plaintext = AesGcmEnvelope.Open(Key(IdentityKey), Convert.FromBase64String(IdentityStored), "curto");

        Assert.Equal("material-de-teste-218", Encoding.ASCII.GetString(plaintext));
    }

    [Fact]
    public void WithAssociatedData_OpensOnlyWithTheSameData()
    {
        var key = Key(McpKey);
        var stored = AesGcmEnvelope.Seal(key, "segredo"u8, "usuario-a"u8);

        Assert.Equal("segredo"u8.ToArray(), AesGcmEnvelope.Open(key, stored, "curto", "usuario-a"u8));
        Assert.ThrowsAny<CryptographicException>(() => AesGcmEnvelope.Open(key, stored, "curto", "usuario-b"u8));
        Assert.ThrowsAny<CryptographicException>(() => AesGcmEnvelope.Open(key, stored, "curto"));
    }

    [Fact]
    public void TooShort_ThrowsWithTheCallersMessage()
    {
        var exception = Assert.Throws<CryptographicException>(() => AesGcmEnvelope.Open(Key(McpKey), new byte[27], "curto demais aqui"));

        Assert.Equal("curto demais aqui", exception.Message);
    }

    [Fact]
    public void KeyMessages_NameTheConfiguration()
    {
        Assert.Equal("sem chave", Assert.Throws<InvalidOperationException>(() => AesGcmEnvelope.ParseKey("X:Y", " ", "sem chave")).Message);
        Assert.Equal("X:Y não é uma string base64 válida.",
            Assert.Throws<InvalidOperationException>(() => AesGcmEnvelope.ParseKey("X:Y", "não é base64!", "sem chave")).Message);
        Assert.Equal("X:Y deve decodificar para 32 bytes (AES-256); decodificou para 16 bytes.",
            Assert.Throws<InvalidOperationException>(() => AesGcmEnvelope.ParseKey("X:Y", Convert.ToBase64String(new byte[16]), "sem chave")).Message);
    }
}
