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
}
