using System.Text.Json.Serialization;

namespace Buteco.Api.McpServers.Entities;

[JsonConverter(typeof(JsonStringEnumConverter<McpServerAuthType>))]
public enum McpServerAuthType
{
    None,
    BearerToken,

    // Token fixo num header de nome configurável (McpServer.AuthHeaderName),
    // enviado cru, sem prefixo e sem Authorization (change
    // mcp-autenticacao-header-customizado, D1). O NOME DO MEMBRO é o contrato:
    // vai assim para o JSON (JsonStringEnumConverter acima) e para a coluna
    // AuthType (HasConversion<string>() em AppDbContext). Renomeá-lo quebra a
    // leitura das linhas já gravadas — e o espelho em apps/workers precisa ter o
    // mesmo nome (teste de acordo em tests/CrossAppTaskStoreCompatibility.Tests).
    CustomHeader,
}
