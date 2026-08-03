using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace Buteco.Workers.Mcp;

/// <summary>
/// Conjunto de tools MCP resolvido para uma execução, junto da posse do
/// ciclo de vida das conexões MCP subjacentes (design.md, Decision 4): as
/// tools em <see cref="Tools"/> permanecem chamáveis enquanto este objeto não
/// for descartado — precisa ficar vivo durante toda a chamada de
/// <c>ChatClientAgent.RunAsync</c>, não só durante a resolução que o
/// precede, porque cada <see cref="ModelContextProtocol.Client.McpClientTool"/>
/// mantém uma referência ao <see cref="McpClient"/> que o descobriu.
/// </summary>
public sealed class McpToolSet(
    IReadOnlyList<AITool> tools,
    IReadOnlyList<(McpClient Client, HttpClientTransport Transport)> connections) : IAsyncDisposable
{
    public IReadOnlyList<AITool> Tools { get; } = tools;

    public async ValueTask DisposeAsync()
    {
        foreach (var (client, transport) in connections)
        {
            await client.DisposeAsync();
            await transport.DisposeAsync();
        }
    }
}
