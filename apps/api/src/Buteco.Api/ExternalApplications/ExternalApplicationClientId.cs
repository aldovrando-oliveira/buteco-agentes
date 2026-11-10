using System.Security.Cryptography;
using Buteco.Api.Identity.Clients;

namespace Buteco.Api.ExternalApplications;

/// <summary>Gera o <c>client_id</c> de uma aplicação nova; trocável no teste (2.2 do tasks.md).</summary>
public interface IExternalApplicationClientIdGenerator
{
    string Next();
}

/// <summary>
/// O <c>client_id</c> da aplicação externa (design.md da change aplicacoes-externas-credencial, D3;
/// Q3): <c>btc_app_</c> mais 8 caracteres de um alfabeto de 32 símbolos sem <c>0</c>, <c>1</c>,
/// <c>l</c> e <c>o</c>, por gerador criptográfico. São 40 bits: não é segredo, o tamanho é para não
/// colidir, e o índice único com nova tentativa fecha o resto. Nenhuma rota recebe <c>client_id</c>.
/// </summary>
public sealed class ExternalApplicationClientId : IExternalApplicationClientIdGenerator
{
    public const string Prefix = "btc_app_";

    public const string Alphabet = "23456789abcdefghijkmnpqrstuvwxyz";

    public const int RandomLength = 8;

    public string Next() => Prefix + RandomNumberGenerator.GetString(Alphabet, RandomLength);

    /// <summary>
    /// O prefixo <c>buteco-</c> é dos clientes do próprio Buteco (decisão do dono, 24/10/2026, na #186):
    /// o <c>apps/inbox</c> e o <c>apps/connectors</c> mapeiam <c>buteco-painel</c> para <c>operator</c>, e o
    /// <c>apps/api</c> mapeia os <c>buteco-*</c> da tabela fechada para os subjects deles. O gerador nunca o
    /// produz; esta recusa é a defesa no ponto de gravar, para um gerador trocado ou errado não criar um
    /// cliente com o prefixo. Lança, e nada é gravado.
    /// </summary>
    public static string Ensure(string clientId) =>
        clientId.StartsWith(IdentityClients.ReservedPrefix, StringComparison.OrdinalIgnoreCase)
            ? throw new InvalidOperationException(
                $"client_id '{clientId}' usa o prefixo reservado {IdentityClients.ReservedPrefix}; aplicação externa não pode recebê-lo.")
            : clientId;
}
