using System.Security.Cryptography;
using Buteco.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Buteco.Api.McpServers.Security;

/// <summary>
/// AES-GCM com nonce aleatório de 96 bits por chamada, chave única de 256
/// bits vinda de configuração/env var (nunca do banco — mesmo padrão já
/// usado para chaves de provedor de LLM). Formato persistido: base64 de
/// <c>nonce (12 bytes) || ciphertext || tag (16 bytes)</c>, pelo
/// <see cref="AesGcmEnvelope"/>, sem dado associado: a cópia do <c>apps/workers</c> lê o
/// mesmo valor.
/// </summary>
public sealed class AesGcmMcpCredentialCipher : IMcpCredentialCipher
{
    private readonly byte[] _key;

    public AesGcmMcpCredentialCipher(IOptions<McpCryptoOptions> options)
    {
        _key = AesGcmEnvelope.ParseKey(
            "Mcp:CredentialEncryptionKey",
            options.Value.CredentialEncryptionKey,
            "Mcp:CredentialEncryptionKey não configurado. Gere uma chave AES-256 em base64 (ex.: `openssl rand -base64 32`) e configure via variável de ambiente/configuração — nunca no banco.");
    }

    public string Encrypt(string plaintext) =>
        Convert.ToBase64String(AesGcmEnvelope.Seal(_key, System.Text.Encoding.UTF8.GetBytes(plaintext)));

    public string Decrypt(string stored)
    {
        byte[] storedBytes;
        try
        {
            storedBytes = Convert.FromBase64String(stored);
        }
        catch (FormatException exception)
        {
            throw new CryptographicException("Valor persistido não é uma string base64 válida.", exception);
        }

        return System.Text.Encoding.UTF8.GetString(
            AesGcmEnvelope.Open(_key, storedBytes, "Valor persistido é curto demais para conter nonce, ciphertext e tag."));
    }
}
