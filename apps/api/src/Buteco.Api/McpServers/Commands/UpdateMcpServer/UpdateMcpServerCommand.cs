using Buteco.Api.McpServers.Entities;
using Mediator;

namespace Buteco.Api.McpServers.Commands.UpdateMcpServer;

public sealed record UpdateMcpServerCommand(
    Guid Id,
    string Name,
    string Description,
    string Url,
    McpServerAuthType AuthType,
    string? Credential) : ICommand<UpdateMcpServerResult>;
