using System.Text;
using Buteco.Inbox.Channels.Adapters.Waha;
using Buteco.Inbox.Orchestration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Buteco.Inbox.Tests;

public class WahaInboundWebhookHandlerTests
{
    private static HttpRequest BuildRequest(string json)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return context.Request;
    }

    // WahaInboundWebhookHandler recebe IServiceScopeFactory, não
    // IInboundMessageOrchestrator direto (design.md, Decision 5 — detalhe
    // de implementação: o handler é Singleton, o orchestrator é Scoped).
    // Um ServiceProvider mínimo, com o mock registrado, dá um
    // IServiceScopeFactory de verdade sem precisar de um host completo.
    private static WahaInboundWebhookHandler BuildHandler(Mock<IInboundMessageOrchestrator> orchestratorMock)
    {
        var services = new ServiceCollection();
        services.AddSingleton(orchestratorMock.Object);
        var provider = services.BuildServiceProvider();
        return new WahaInboundWebhookHandler(provider.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task HandleAsync_MessageEvent_ExtractsBodyExactlyAndKeepsExternalIdSuffix()
    {
        // Fixture baseada no payload real confirmado na investigação
        // (design.md, Context) — inclui um campo "text" divergente de
        // propósito, pra provar que o handler lê "body", não "text"
        // (design.md, Decision 5).
        const string json = """
            {
              "event": "message",
              "session": "default",
              "payload": {
                "id": "true_5511999999999@c.us_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                "timestamp": 1667561485,
                "from": "5511999999999@c.us",
                "fromMe": false,
                "to": "5511888888888@c.us",
                "body": "Olá, preciso de ajuda",
                "text": "valor de um campo text espúrio, não deve ser usado",
                "hasMedia": false,
                "ack": 1
              }
            }
            """;

        var orchestratorMock = new Mock<IInboundMessageOrchestrator>();
        var channelId = Guid.NewGuid();
        var handler = BuildHandler(orchestratorMock);

        await handler.HandleAsync(channelId, BuildRequest(json), CancellationToken.None);

        orchestratorMock.Verify(o => o.ReceiveMessageAsync(
            channelId,
            "5511999999999@c.us",
            "Olá, preciso de ajuda",
            It.IsAny<DateTimeOffset>(),
            It.Is<IReadOnlyDictionary<string, string>>(metadata => metadata["phone"] == "5511999999999"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_NonMessageEvent_DoesNotInvokeOrchestrator()
    {
        const string json = """
            {
              "event": "session.status",
              "session": "default",
              "payload": {
                "status": "WORKING"
              }
            }
            """;

        var orchestratorMock = new Mock<IInboundMessageOrchestrator>();
        var handler = BuildHandler(orchestratorMock);

        await handler.HandleAsync(Guid.NewGuid(), BuildRequest(json), CancellationToken.None);

        orchestratorMock.Verify(o => o.ReceiveMessageAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
