using Buteco.Api.McpServers.Connectivity;
using Mediator;

namespace Buteco.Api.McpServers.Commands.TestUnsavedMcpServerConnection;

public sealed class TestUnsavedMcpServerConnectionCommandHandler(IMcpConnectionTester connectionTester)
    : ICommandHandler<TestUnsavedMcpServerConnectionCommand, McpConnectionTestResult>
{
    public ValueTask<McpConnectionTestResult> Handle(TestUnsavedMcpServerConnectionCommand command, CancellationToken cancellationToken)
    {
        return new ValueTask<McpConnectionTestResult>(
            connectionTester.TestAsync(command.Url, command.AuthType, command.Credential, cancellationToken));
    }
}
