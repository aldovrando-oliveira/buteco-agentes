using Buteco.Api.McpServers.Connectivity;
using Buteco.Api.McpServers.Entities;
using Mediator;

namespace Buteco.Api.McpServers.Commands.TestUnsavedMcpServerConnection;

public sealed record TestUnsavedMcpServerConnectionCommand(
    string Url,
    McpServerAuthType AuthType,
    string? Credential) : ICommand<McpConnectionTestResult>;
