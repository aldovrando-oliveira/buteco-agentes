using Buteco.Api.McpServers.Responses;

namespace Buteco.Api.McpServers.Commands.UpdateMcpServer;

/// <summary>
/// Sinal neutro devolvido pelo handler — sem nenhuma referência a
/// <c>Microsoft.AspNetCore.Http.HttpResults</c> (mesmo padrão de
/// <c>UpdateAgentResult</c>). <see cref="MissingCredential"/> cobre o caso
/// em que a atualização deixaria o registro com <c>AuthType != None</c> e
/// nenhuma credencial (nem uma nova enviada, nem uma já persistida) —
/// mesma regra de "BearerToken exige credencial" já aplicada no cadastro,
/// estendida para o estado final da atualização.
/// </summary>
public sealed record UpdateMcpServerResult(McpServerResponse? McpServer, bool Found, bool MissingCredential)
{
    public static UpdateMcpServerResult NotFound() => new(null, Found: false, MissingCredential: false);

    public static UpdateMcpServerResult CredentialMissing() => new(null, Found: true, MissingCredential: true);

    public static UpdateMcpServerResult Success(McpServerResponse mcpServer) => new(mcpServer, Found: true, MissingCredential: false);
}
