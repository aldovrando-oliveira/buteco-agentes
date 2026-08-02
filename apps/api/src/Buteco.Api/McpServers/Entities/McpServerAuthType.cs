using System.Text.Json.Serialization;

namespace Buteco.Api.McpServers.Entities;

[JsonConverter(typeof(JsonStringEnumConverter<McpServerAuthType>))]
public enum McpServerAuthType
{
    None,
    BearerToken,
}
