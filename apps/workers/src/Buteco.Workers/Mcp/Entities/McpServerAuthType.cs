using System.Text.Json.Serialization;

namespace Buteco.Workers.Mcp.Entities;

/// <summary>
/// Mirror do enum de <c>apps/api</c> (<c>McpServers/Entities/McpServerAuthType.cs</c>)
/// — mesmo padrão de isolamento já usado para <see cref="Agents.Entities.Agent"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<McpServerAuthType>))]
public enum McpServerAuthType
{
    None,
    BearerToken,

    // Mesmo nome do membro de apps/api: o nome É o contrato na coluna AuthType
    // (change mcp-autenticacao-header-customizado, D1). O teste de acordo em
    // tests/CrossAppTaskStoreCompatibility.Tests lê, por este contexto, cada
    // valor gravado pelo de apps/api e compara o NOME.
    CustomHeader,
}
