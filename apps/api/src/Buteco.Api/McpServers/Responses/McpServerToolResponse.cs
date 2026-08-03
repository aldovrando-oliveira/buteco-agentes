using Buteco.Api.McpServers.Connectivity;

namespace Buteco.Api.McpServers.Responses;

public sealed record McpServerToolResponse(string Name, string Description)
{
    public static McpServerToolResponse FromDescriptor(McpToolDescriptor descriptor) => new(descriptor.Name, descriptor.Description);
}
