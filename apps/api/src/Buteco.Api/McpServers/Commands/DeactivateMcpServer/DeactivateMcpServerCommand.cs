using Buteco.Api.McpServers.Responses;
using Mediator;

namespace Buteco.Api.McpServers.Commands.DeactivateMcpServer;

public sealed record DeactivateMcpServerCommand(Guid Id) : ICommand<McpServerResponse?>;
