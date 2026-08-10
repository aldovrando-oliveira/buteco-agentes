using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Orchestration;
using Buteco.Inbox.Orchestration.Entities;
using Buteco.Inbox.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Tests;

// OrchestrationFactoryFixture roda com janela de debounce curta (300ms) e
// IA2AClientFactory substituído por FakeA2AClientFactory (tasks.md 6.2-6.4.1)
// — DebounceSweepService real disparando de verdade, sem rede real.
public class DebounceSweepServiceTests(OrchestrationFactoryFixture factory) : IClassFixture<OrchestrationFactoryFixture>
{
    [Fact]
    public async Task MessagesWithinWindow_TriggerSingleSendMessage_WithAgentIdFromChannelAndContextIdFromSession()
    {
        factory.A2AClientFactory.Handler = FakeA2AClientFactory.DefaultHandler;

        var agentId = Guid.NewGuid();
        var channelId = await CreateChannelAsync(agentId);
        var externalId = UniqueExternalId();

        await ReceiveAsync(channelId, externalId, "Primeira");
        await Task.Delay(TimeSpan.FromMilliseconds(80));
        await ReceiveAsync(channelId, externalId, "Segunda");

        var expectedContextId = await ResolveContextIdAsync(channelId, externalId);

        await PollUntil(
            () => factory.A2AClientFactory.Requests.Count(r => r.Message.ContextId == expectedContextId),
            count => count >= 1,
            TimeSpan.FromSeconds(3));

        // Espera mais um pouco para confirmar que não vem um segundo
        // disparo espúrio para a mesma sessão — as duas mensagens tinham
        // que ter sido agrupadas num único SendMessage.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        var requestsForSession = factory.A2AClientFactory.Requests
            .Where(r => r.Message.ContextId == expectedContextId)
            .ToList();

        var request = Assert.Single(requestsForSession);
        Assert.Contains("Primeira", request.Message.Parts[0].Text);
        Assert.Contains("Segunda", request.Message.Parts[0].Text);

        // Isolado da prova de round-trip ponta a ponta (7.2, mais cara):
        // se a correspondência AgentId/ContextId quebrar, este teste
        // aponta a causa diretamente.
        Assert.Contains(agentId, factory.A2AClientFactory.RequestedAgentIds);
        Assert.Equal(expectedContextId, request.Message.ContextId);
    }

    [Fact]
    public async Task MessagesOutsideWindow_TriggerSeparateSendMessageCalls()
    {
        factory.A2AClientFactory.Handler = FakeA2AClientFactory.DefaultHandler;

        var channelId = await CreateChannelAsync(Guid.NewGuid());
        var externalId = UniqueExternalId();

        await ReceiveAsync(channelId, externalId, "Mensagem 1");
        var contextId = await ResolveContextIdAsync(channelId, externalId);

        await PollUntil(
            () => factory.A2AClientFactory.Requests.Count(r => r.Message.ContextId == contextId),
            count => count >= 1,
            TimeSpan.FromSeconds(3));

        await ReceiveAsync(channelId, externalId, "Mensagem 2");

        await PollUntil(
            () => factory.A2AClientFactory.Requests.Count(r => r.Message.ContextId == contextId),
            count => count >= 2,
            TimeSpan.FromSeconds(3));

        var texts = factory.A2AClientFactory.Requests
            .Where(r => r.Message.ContextId == contextId)
            .Select(r => r.Message.Parts[0].Text)
            .ToList();

        Assert.Equal(2, texts.Count);
        Assert.Contains("Mensagem 1", texts);
        Assert.Contains("Mensagem 2", texts);
    }

    [Fact]
    public async Task SendMessage_ReturnsRejectedTask_ClosesDispatchWithoutAwaitingPushNotification()
    {
        factory.A2AClientFactory.Handler = FakeA2AClientFactory.RejectedHandler;

        var agentId = Guid.NewGuid();
        var channelId = await CreateChannelAsync(agentId);
        var externalId = UniqueExternalId();

        await ReceiveAsync(channelId, externalId, "Mensagem para agente inativo");
        var sessionId = await ResolveSessionIdAsync(channelId, externalId);

        await PollUntil(
            () => factory.A2AClientFactory.RequestedAgentIds.Count(id => id == agentId),
            count => count >= 1,
            TimeSpan.FromSeconds(3));

        await PollUntilNoPendingDispatchAsync(sessionId, TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task TransportFailure_RetriesOnNextSweep_SucceedsOnSecondAttempt()
    {
        var attempt = 0;
        factory.A2AClientFactory.Handler = request =>
        {
            var current = Interlocked.Increment(ref attempt);
            return current == 1
                ? throw new HttpRequestException("Simulated unreachable host.")
                : FakeA2AClientFactory.DefaultHandler(request);
        };

        var channelId = await CreateChannelAsync(Guid.NewGuid());
        var externalId = UniqueExternalId();
        await ReceiveAsync(channelId, externalId, "Mensagem com falha transitória");
        var sessionId = await ResolveSessionIdAsync(channelId, externalId);

        var pendingDispatch = await PollUntilAsync(
            () => GetPendingDispatchAsync(sessionId),
            dispatch => dispatch is { Status: PendingDispatchStatus.Dispatching, TaskId: not null },
            TimeSpan.FromSeconds(5));

        Assert.NotNull(pendingDispatch);
        Assert.Equal(1, pendingDispatch!.AttemptCount);
        Assert.True(attempt >= 2);
    }

    [Fact]
    public async Task TransportFailure_ExhaustsMaxAttempts_MarksFailedAndRemoves()
    {
        var agentId = Guid.NewGuid();
        factory.A2AClientFactory.Handler = _ => throw new HttpRequestException("Simulated unreachable host.");

        var channelId = await CreateChannelAsync(agentId);
        var externalId = UniqueExternalId();
        await ReceiveAsync(channelId, externalId, "Mensagem sempre falha");
        var sessionId = await ResolveSessionIdAsync(channelId, externalId);

        // MaxDispatchAttempts = 3 (OrchestrationFactoryFixture) — a
        // PendingDispatch só é removida depois da terceira falha, não na
        // primeira (design.md, Decisão 9).
        await PollUntilNoPendingDispatchAsync(sessionId, TimeSpan.FromSeconds(8));

        Assert.True(factory.A2AClientFactory.RequestedAgentIds.Count(id => id == agentId) >= 3);
    }

    private async Task ReceiveAsync(Guid channelId, string externalId, string text)
    {
        using var scope = factory.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IInboundMessageOrchestrator>();
        await orchestrator.ReceiveMessageAsync(channelId, externalId, text, DateTimeOffset.UtcNow, CancellationToken.None);
    }

    private async Task<string> ResolveContextIdAsync(Guid channelId, string externalId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var contact = await dbContext.Contacts.AsNoTracking().SingleAsync(c => c.ChannelId == channelId && c.ExternalId == externalId);
        var session = await dbContext.Sessions.AsNoTracking().SingleAsync(s => s.ContactId == contact.Id);
        return session.ContextId;
    }

    private async Task<Guid> ResolveSessionIdAsync(Guid channelId, string externalId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var contact = await dbContext.Contacts.AsNoTracking().SingleAsync(c => c.ChannelId == channelId && c.ExternalId == externalId);
        var session = await dbContext.Sessions.AsNoTracking().SingleAsync(s => s.ContactId == contact.Id);
        return session.Id;
    }

    private async Task<PendingDispatch?> GetPendingDispatchAsync(Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.PendingDispatches.AsNoTracking().SingleOrDefaultAsync(d => d.SessionId == sessionId);
    }

    private async Task PollUntilNoPendingDispatchAsync(Guid sessionId, TimeSpan timeout) =>
        await PollUntilAsync(
            () => GetPendingDispatchAsync(sessionId),
            dispatch => dispatch is null,
            timeout);

    private static async Task<T> PollUntil<T>(Func<T> probe, Func<T, bool> isDone, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var value = probe();
            if (isDone(value))
            {
                return value;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condição não satisfeita a tempo.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    private static async Task<T> PollUntilAsync<T>(Func<Task<T>> probeAsync, Func<T, bool> isDone, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var value = await probeAsync();
            if (isDone(value))
            {
                return value;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condição não satisfeita a tempo.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    private static string UniqueExternalId() => $"+5511{Guid.NewGuid():N}"[..15];

    private async Task<Guid> CreateChannelAsync(Guid agentId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var channel = new Channel(ChannelType.WhatsApp, $"Canal {Guid.NewGuid()}", "irrelevante-nesta-fatia", agentId);
        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync();
        return channel.Id;
    }
}
