using Buteco.Api.McpServers.Connectivity;
using Buteco.Api.McpServers.Entities;
using Buteco.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests;

public class McpConnectionTesterListToolsTests(McpConnectionTestFixture factory) : IClassFixture<McpConnectionTestFixture>
{
    [Fact]
    public async Task ListToolsAsync_HandshakeSuccessful_ReturnsToolsFromServer()
    {
        factory.McpServerHandler.SimulateUnreachable = false;
        factory.McpServerHandler.RequiredBearerToken = null;
        factory.McpServerHandler.AvailableTools =
        [
            new FakeMcpTool("read_file", "Lê um arquivo"),
            new FakeMcpTool("write_file", "Escreve em um arquivo"),
        ];

        using var scope = factory.Services.CreateScope();
        var connectionTester = scope.ServiceProvider.GetRequiredService<IMcpConnectionTester>();

        var result = await connectionTester.ListToolsAsync("https://mcp.example.com", McpServerAuthType.None, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Tools);
        Assert.Equal(2, result.Tools!.Count);
        Assert.Contains(result.Tools, tool => tool.Name == "read_file" && tool.Description == "Lê um arquivo");
        Assert.Contains(result.Tools, tool => tool.Name == "write_file" && tool.Description == "Escreve em um arquivo");

        factory.McpServerHandler.AvailableTools = [];
    }

    [Fact]
    public async Task ListToolsAsync_HostUnreachable_ReturnsFailureWithHostUnreachableReason()
    {
        factory.McpServerHandler.SimulateUnreachable = true;
        factory.McpServerHandler.RequiredBearerToken = null;

        using var scope = factory.Services.CreateScope();
        var connectionTester = scope.ServiceProvider.GetRequiredService<IMcpConnectionTester>();

        var result = await connectionTester.ListToolsAsync("https://unreachable.example.com", McpServerAuthType.None, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Tools);
        Assert.Equal(McpConnectionTestFailureReason.HostUnreachable, result.FailureReason);

        factory.McpServerHandler.SimulateUnreachable = false;
    }

    [Fact]
    public async Task ListToolsAsync_CredentialRejected_ReturnsFailureWithCredentialRejectedReason()
    {
        factory.McpServerHandler.SimulateUnreachable = false;
        factory.McpServerHandler.RequiredBearerToken = "expected-token";

        using var scope = factory.Services.CreateScope();
        var connectionTester = scope.ServiceProvider.GetRequiredService<IMcpConnectionTester>();

        var result = await connectionTester.ListToolsAsync("https://mcp.example.com", McpServerAuthType.BearerToken, "wrong-token", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Tools);
        Assert.Equal(McpConnectionTestFailureReason.CredentialRejected, result.FailureReason);

        factory.McpServerHandler.RequiredBearerToken = null;
    }
}
