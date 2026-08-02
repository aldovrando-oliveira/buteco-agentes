using Buteco.Api.McpServers.Responses;
using Mediator;

namespace Buteco.Api.McpServers.Commands.ActivateMcpServer;

public sealed record ActivateMcpServerCommand(Guid Id) : ICommand<McpServerResponse?>;
