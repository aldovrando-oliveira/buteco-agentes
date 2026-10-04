extern alias ApiAssembly;
extern alias InboxAssembly;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InboxOrchestratorRoundTrip.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using InboxAppDbContext = InboxAssembly::Buteco.Inbox.Infrastructure.AppDbContext;
using InboxChannel = InboxAssembly::Buteco.Inbox.Channels.Entities.Channel;
using InboxInboundMessageOrchestrator = InboxAssembly::Buteco.Inbox.Orchestration.IInboundMessageOrchestrator;
using InboxMessageContentType = InboxAssembly::Buteco.Inbox.Messages.Entities.MessageContentType;
using InboxPendingDispatch = InboxAssembly::Buteco.Inbox.Orchestration.Entities.PendingDispatch;
using InboxPendingDispatchStatus = InboxAssembly::Buteco.Inbox.Orchestration.Entities.PendingDispatchStatus;
using InboxCredentialCipher = InboxAssembly::Buteco.Inbox.Channels.Security.IChannelCredentialCipher;
using InboxMessage = InboxAssembly::Buteco.Inbox.Messages.Entities.Message;
using InboxMessageDeliveryStatus = InboxAssembly::Buteco.Inbox.Messages.Entities.MessageDeliveryStatus;
using InboxMessageDirection = InboxAssembly::Buteco.Inbox.Messages.Entities.MessageDirection;
using InboxMessageDispatchStatus = InboxAssembly::Buteco.Inbox.Messages.Entities.MessageDispatchStatus;

namespace InboxOrchestratorRoundTrip.Tests;

/// <summary>
/// Fontes 2 e 3 da #47 com os três apps reais: a task fica terminal em apps/api,
/// o push não chega ao inbox, e a reconciliação resolve a linha pelo GetTask real
/// (design.md, D3/D10). Antes da correção, a linha ficava em Dispatching.
/// </summary>
public class OrphanDispatchRoundTripTests(RoundTripFixture fixture) : IClassFixture<RoundTripFixture>
{
    private const string FailureNotice = "Não consegui responder agora. Pode tentar de novo em instantes?";

    private static readonly TimeSpan ReconciliationTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Source2_PushFails_TaskCompleted_IsReconciled_AndTheReplyIsDelivered()
    {
        fixture.ChatClientFails = false;
        fixture.PushGate.Reset(PushNotificationGateMode.Fail);
        try
        {
            var (agentId, channelId, externalId) = await StartConversationAsync();
            var taskId = await WaitForTaskIdAsync(channelId, externalId);

            await PollUntilAsync(() => HasPendingDispatchAsync(channelId, externalId), has => !has, ReconciliationTimeout);

            Assert.Equal("TASK_STATE_COMPLETED", await GetTaskStateAsync(agentId, taskId));
            Assert.Contains("failed", fixture.PushGate.Outcomes);
            var messages = await GetMessagesAsync(channelId, externalId);
            Assert.Equal(InboxMessageDispatchStatus.Completed, Assert.Single(messages, m => m.Direction == InboxMessageDirection.Inbound).DispatchStatus);
            var outbound = Assert.Single(messages, m => m.Direction == InboxMessageDirection.Outbound);
            Assert.Equal(RoundTripFixture.MockedAgentReplyText, outbound.Content);
            Assert.Equal(InboxMessageDeliveryStatus.Sent, outbound.DeliveryStatus);
        }
        finally
        {
            fixture.PushGate.Reset(PushNotificationGateMode.PassThrough);
        }
    }

    [Fact]
    public async Task Question2_WorkerFailurePath_SendsPush_AndDispatchIsResolved()
    {
        fixture.ChatClientFails = true;
        fixture.PushGate.Reset(PushNotificationGateMode.PassThrough);
        try
        {
            var (agentId, channelId, externalId) = await StartConversationAsync();
            var taskId = await WaitForTaskIdAsync(channelId, externalId);

            await PollUntilAsync(() => HasPendingDispatchAsync(channelId, externalId), has => !has, TimeSpan.FromSeconds(20));

            Assert.Equal("TASK_STATE_FAILED", await GetTaskStateAsync(agentId, taskId));
            Assert.Contains("delivered:200", fixture.PushGate.Outcomes);
            await AssertFailureNoticeAsync(channelId, externalId);
        }
        finally
        {
            fixture.ChatClientFails = false;
        }
    }

    [Fact]
    public async Task Source3_WorkerStopsBetweenTerminalWriteAndPush_IsReconciled_AndTheReplyIsDelivered()
    {
        fixture.ChatClientFails = false;
        fixture.PushGate.Reset(PushNotificationGateMode.HoldUntilCancelled);
        string taskId;
        Guid agentId, channelId;
        string externalId;
        try
        {
            (agentId, channelId, externalId) = await StartConversationAsync();
            taskId = await WaitForTaskIdAsync(channelId, externalId);

            // O push só é chamado DEPOIS de a task estar gravada terminal.
            await fixture.PushGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal("TASK_STATE_COMPLETED", await GetTaskStateAsync(agentId, taskId));

            fixture.PushGate.Reset(PushNotificationGateMode.PassThrough);
            await fixture.RestartWorkersAsync();
        }
        finally
        {
            fixture.PushGate.Reset(PushNotificationGateMode.PassThrough);
        }

        // Sentinela: conversa nova publicada DEPOIS do reinício. Com um
        // consumidor e prefetch 1, quando ela fecha o round-trip a mensagem
        // devolvida à fila na parada já foi reentregue e confirmada.
        var (_, sentinelChannelId, sentinelExternalId) = await StartConversationAsync();
        await WaitForTaskIdAsync(sentinelChannelId, sentinelExternalId);
        await PollUntilAsync(() => HasPendingDispatchAsync(sentinelChannelId, sentinelExternalId), has => !has, TimeSpan.FromSeconds(20));

        await PollUntilAsync(() => HasPendingDispatchAsync(channelId, externalId), has => !has, ReconciliationTimeout);
        Assert.Equal("TASK_STATE_COMPLETED", await GetTaskStateAsync(agentId, taskId));
        Assert.Contains("cancelled", fixture.PushGate.Outcomes);
        var outbound = Assert.Single(await GetMessagesAsync(channelId, externalId), m => m.Direction == InboxMessageDirection.Outbound);
        Assert.Equal(RoundTripFixture.MockedAgentReplyText, outbound.Content);
    }

    [Fact]
    public async Task Source2_PushFails_TaskFailed_IsReconciled_WithTheFailureNotice()
    {
        fixture.ChatClientFails = true;
        fixture.PushGate.Reset(PushNotificationGateMode.Fail);
        try
        {
            var (agentId, channelId, externalId) = await StartConversationAsync();
            var taskId = await WaitForTaskIdAsync(channelId, externalId);

            await PollUntilAsync(() => HasPendingDispatchAsync(channelId, externalId), has => !has, ReconciliationTimeout);

            Assert.Equal("TASK_STATE_FAILED", await GetTaskStateAsync(agentId, taskId));
            await AssertFailureNoticeAsync(channelId, externalId);
        }
        finally
        {
            fixture.ChatClientFails = false;
            fixture.PushGate.Reset(PushNotificationGateMode.PassThrough);
        }
    }

    [Theory]
    [InlineData(false, "TASK_STATE_COMPLETED")]
    [InlineData(true, "TASK_STATE_FAILED")]
    public async Task TerminalTask_CarriesStatusTimestamp_InRawPersistedJson(bool chatFails, string expectedState)
    {
        fixture.ChatClientFails = chatFails;
        fixture.PushGate.Reset(PushNotificationGateMode.PassThrough);
        try
        {
            var (agentId, channelId, externalId) = await StartConversationAsync();
            var taskId = await WaitForTaskIdAsync(channelId, externalId);
            await PollUntilAsync(() => HasPendingDispatchAsync(channelId, externalId), has => !has, TimeSpan.FromSeconds(20));

            var status = await GetRawStatusAsync(agentId, taskId);
            Console.WriteLine($"MEDIDA status={status}");
            Assert.Equal(expectedState, status.GetProperty("state").GetString());
            Assert.True(status.TryGetProperty("timestamp", out var timestamp));
            Assert.Equal(JsonValueKind.String, timestamp.ValueKind);
        }
        finally
        {
            fixture.ChatClientFails = false;
        }
    }

    private async Task<JsonElement> GetRawStatusAsync(Guid agentId, string taskId)
    {
        var token = await fixture.LoginAsOperatorAsync();
        var client = fixture.ApiFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var payload = new { jsonrpc = "2.0", id = 1, method = "GetTask", @params = new { id = taskId } };

        var response = await client.PostAsJsonAsync($"/agents/{agentId}/a2a", payload);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("result").GetProperty("status").Clone();
    }

    private async Task<(Guid AgentId, Guid ChannelId, string ExternalId)> StartConversationAsync()
    {
        var agentId = await CreateAgentAsync();
        var (channelId, externalId) = await CreateChannelAsync(agentId);

        using var scope = fixture.InboxFactory.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<InboxInboundMessageOrchestrator>();
        await orchestrator.ReceiveMessageAsync(
            channelId,
            externalId,
            "Olá, preciso de ajuda",
            InboxMessageContentType.Text,
            Guid.NewGuid().ToString(),
            displayName: null,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            CancellationToken.None);

        return (agentId, channelId, externalId);
    }

    private async Task<string> WaitForTaskIdAsync(Guid channelId, string externalId) =>
        (await PollUntilAsync(
            async () => (await FindPendingDispatchAsync(channelId, externalId))?.TaskId,
            id => id is not null,
            TimeSpan.FromSeconds(10)))!;

    private async Task AssertFailureNoticeAsync(Guid channelId, string externalId)
    {
        var messages = await GetMessagesAsync(channelId, externalId);
        Assert.Equal(InboxMessageDispatchStatus.Failed, Assert.Single(messages, m => m.Direction == InboxMessageDirection.Inbound).DispatchStatus);
        var outbound = Assert.Single(messages, m => m.Direction == InboxMessageDirection.Outbound);
        Assert.Equal(FailureNotice, outbound.Content);
        Assert.Equal(InboxMessageDeliveryStatus.Sent, outbound.DeliveryStatus);
    }

    private async Task<List<InboxMessage>> GetMessagesAsync(Guid channelId, string externalId)
    {
        using var scope = fixture.InboxFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InboxAppDbContext>();
        var contact = await dbContext.Contacts.AsNoTracking().SingleAsync(c => c.ChannelId == channelId && c.ExternalId == externalId);
        var sessionIds = await dbContext.Sessions.AsNoTracking().Where(s => s.ContactId == contact.Id).Select(s => s.Id).ToListAsync();
        return await dbContext.Messages.AsNoTracking().Where(m => sessionIds.Contains(m.SessionId)).ToListAsync();
    }

    private async Task<Guid> CreateAgentAsync()
    {
        var token = await fixture.LoginAsOperatorAsync();
        var client = fixture.ApiFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/agents",
            new { name = "Agente Órfã", instructions = "Responda com simpatia.", provider = "openai", model = "gpt-5.6-sol" });
        response.EnsureSuccessStatusCode();

        var agent = await response.Content.ReadFromJsonAsync<JsonElement>();
        return agent.GetProperty("id").GetGuid();
    }

    private async Task<(Guid ChannelId, string ExternalId)> CreateChannelAsync(Guid agentId)
    {
        using var scope = fixture.InboxFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InboxAppDbContext>();

        // Credencial cifrada de verdade: a entrega ao canal decifra antes de chamar
        // o sender, e um literal faria toda entrega persistir como falha.
        var cipher = scope.ServiceProvider.GetRequiredService<InboxCredentialCipher>();
        var channel = new InboxChannel("test-channel", $"Canal Órfã {Guid.NewGuid()}", cipher.Encrypt("credencial"), agentId);
        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync();

        return (channel.Id, $"+5511{Guid.NewGuid():N}"[..15]);
    }

    private async Task<bool> HasPendingDispatchAsync(Guid channelId, string externalId) =>
        await FindPendingDispatchAsync(channelId, externalId) is not null;

    private async Task<InboxPendingDispatch?> FindPendingDispatchAsync(Guid channelId, string externalId)
    {
        using var scope = fixture.InboxFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InboxAppDbContext>();

        var contact = await dbContext.Contacts.AsNoTracking()
            .SingleOrDefaultAsync(c => c.ChannelId == channelId && c.ExternalId == externalId);
        if (contact is null)
        {
            return null;
        }

        var sessionIds = await dbContext.Sessions.AsNoTracking()
            .Where(s => s.ContactId == contact.Id)
            .Select(s => s.Id)
            .ToListAsync();

        return await dbContext.PendingDispatches.AsNoTracking()
            .Where(d => sessionIds.Contains(d.SessionId))
            .FirstOrDefaultAsync();
    }

    private async Task<string> GetTaskStateAsync(Guid agentId, string taskId)
    {
        var token = await fixture.LoginAsOperatorAsync();
        var client = fixture.ApiFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var payload = new { jsonrpc = "2.0", id = 1, method = "GetTask", @params = new { id = taskId } };

        var response = await client.PostAsJsonAsync($"/agents/{agentId}/a2a", payload);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("result").GetProperty("status").GetProperty("state").GetString()!;
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

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }
}
