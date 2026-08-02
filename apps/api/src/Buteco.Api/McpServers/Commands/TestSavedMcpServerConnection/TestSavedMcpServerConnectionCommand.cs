using Mediator;

namespace Buteco.Api.McpServers.Commands.TestSavedMcpServerConnection;

public sealed record TestSavedMcpServerConnectionCommand(Guid Id) : ICommand<TestSavedMcpServerConnectionResult>;
