using Buteco.Api.McpServers.Entities;
using Buteco.Api.McpServers.Responses;
using Mediator;

namespace Buteco.Api.McpServers.Commands.CreateMcpServer;

public sealed record CreateMcpServerCommand(
    string Name,
    string Description,
    string Url,
    McpServerAuthType AuthType,
    string? Credential) : ICommand<McpServerResponse>;
