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
using InboxCredentialCipher = InboxAssembly::Buteco.Inbox.Channels.Security.IChannelCredentialCipher;
using InboxInboundMessageOrchestrator = InboxAssembly::Buteco.Inbox.Orchestration.IInboundMessageOrchestrator;
using InboxMessage = InboxAssembly::Buteco.Inbox.Messages.Entities.Message;
using InboxMessageContentType = InboxAssembly::Buteco.Inbox.Messages.Entities.MessageContentType;
using InboxMessageDirection = InboxAssembly::Buteco.Inbox.Messages.Entities.MessageDirection;
using InboxOutcomeProcessor = InboxAssembly::Buteco.Inbox.Orchestration.DispatchOutcomeProcessor;
using InboxPendingDispatch = InboxAssembly::Buteco.Inbox.Orchestration.Entities.PendingDispatch;

namespace InboxOrchestratorRoundTrip.Tests;

/// <summary>
/// Guarda 4 da D5 da change inbox-serializacao-por-sessao (#162), com os três apps
/// reais: a segunda mensagem de uma conversa ocupada não falha no lock de contexto
/// do worker.
///
/// <para>
/// Gêmeo de ida e volta de
/// <c>ConversationContextLockFailureTests.TwoInstances_SameContext_SlowFirstTask_BothReachTerminalState</c>
/// (apps/workers), que continua afirmando <c>[Completed, Failed]</c> no nível do
/// worker, porque esse é o contrato para cliente A2A externo (D2/D4). Aqui o
/// caminho é o do inbox, e o inbox não produz mais o cenário: msg2 espera a
/// primeira linha sair de <c>Dispatching</c> antes de virar task.
/// </para>
///
/// <para>
/// Contra o defeito, msg2 vira task enquanto a primeira ainda roda, cai na segunda
/// instância (a primeira está ocupada, <c>prefetchCount: 1</c>), espera o lock por
/// mais que o <c>Command Timeout</c> curto e termina <c>failed</c>, com o aviso de
/// falha entregue ao contato.
/// </para>
/// </summary>
public class SessionSerializationRoundTripTests(RoundTripFixture fixture) : IClassFixture<RoundTripFixture>
{
    // Os mesmos números de ConversationContextLockFailureTests: o LLM leva mais que
    // o Command Timeout da segunda instância, então uma task concorrente no mesmo
    // contexto não consegue o lock a tempo.
    private static readonly TimeSpan SlowLlm = TimeSpan.FromSeconds(8);
    private const int ShortCommandTimeoutSeconds = 3;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task SecondMessage_WhileFirstTaskIsWorking_TwoWorkerInstances_BothCompleteAndBothRepliesAreDelivered()
    {
        fixture.ChatClientDelay = SlowLlm;
        fixture.WorkerCommandTimeoutSeconds = ShortCommandTimeoutSeconds;
        try
        {
            var agentId = await CreateAgentAsync();
            var (channelId, externalId) = await CreateChannelAsync(agentId);

            await ReceiveAsync(channelId, externalId, "msg1");
            var firstTaskId = await PollUntilAsync(
                async () => (await FindDispatchesAsync(channelId, externalId)).Select(d => d.TaskId).FirstOrDefault(id => id is not null),
                id => id is not null,
                Timeout);

            // Ponto de sincronização por estado persistido, como em
            // ConversationContextLockFailureTests.cs:167-172: a primeira instância
            // está ocupada com a primeira task ANTES de a segunda existir. Sem isso,
            // as duas tasks poderiam cair na mesma instância, serializar na fila, e
            // o guarda passaria verde sem exercitar nada.
            await PollUntilAsync(() => GetTaskStateAsync(agentId, firstTaskId!), state => state == "TASK_STATE_WORKING", Timeout);
            await fixture.StartSecondWorkersAsync();

            await ReceiveAsync(channelId, externalId, "msg2");

            var secondTaskId = await PollUntilAsync(
                async () => (await FindDispatchesAsync(channelId, externalId))
                    .Select(d => d.TaskId)
                    .FirstOrDefault(id => id is not null && id != firstTaskId),
                id => id is not null,
                Timeout);

            await PollUntilAsync(async () => (await FindDispatchesAsync(channelId, externalId)).Count, count => count == 0, Timeout);

            // Estados das duas tasks e o que foi entregue ao contato, num retrato só:
            // contra o defeito, a falha mostra as duas coisas de uma vez (a task
            // `failed` e o aviso entregue), e não só a primeira asserção que reprova.
            List<string> observed =
            [
                await GetTaskStateAsync(agentId, firstTaskId!),
                await GetTaskStateAsync(agentId, secondTaskId!),
                .. (await GetMessagesAsync(channelId, externalId))
                    .Where(m => m.Direction == InboxMessageDirection.Outbound)
                    .OrderBy(m => m.OccurredAt)
                    .Select(m => m.Content == InboxOutcomeProcessor.FailureNoticeText ? "aviso de falha" : m.Content),
            ];

            Assert.Equal(
                ["TASK_STATE_COMPLETED", "TASK_STATE_COMPLETED", RoundTripFixture.MockedAgentReplyText, RoundTripFixture.MockedAgentReplyText],
                observed);
        }
        finally
        {
            fixture.ChatClientDelay = TimeSpan.Zero;
            fixture.WorkerCommandTimeoutSeconds = null;
            await fixture.StopSecondWorkersAsync();
        }
    }

    private async Task ReceiveAsync(Guid channelId, string externalId, string text)
    {
        using var scope = fixture.InboxFactory.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<InboxInboundMessageOrchestrator>();
        await orchestrator.ReceiveMessageAsync(
            channelId,
            externalId,
            text,
            InboxMessageContentType.Text,
            Guid.NewGuid().ToString(),
            displayName: null,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            CancellationToken.None);
    }

    private async Task<Guid> CreateAgentAsync()
    {
        var token = await fixture.LoginAsOperatorAsync();
        var client = fixture.ApiFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/agents",
            new { name = "Agente Serialização", instructions = "Responda com simpatia.", provider = "openai", model = "gpt-5.6-sol" });
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
        var channel = new InboxChannel("test-channel", $"Canal Serialização {Guid.NewGuid()}", cipher.Encrypt("credencial"), agentId);
        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync();

        return (channel.Id, $"+5511{Guid.NewGuid():N}"[..15]);
    }

    private async Task<List<InboxPendingDispatch>> FindDispatchesAsync(Guid channelId, string externalId)
    {
        using var scope = fixture.InboxFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InboxAppDbContext>();

        var contact = await dbContext.Contacts.AsNoTracking()
            .SingleOrDefaultAsync(c => c.ChannelId == channelId && c.ExternalId == externalId);
        if (contact is null)
        {
            return [];
        }

        var sessionIds = await dbContext.Sessions.AsNoTracking()
            .Where(s => s.ContactId == contact.Id)
            .Select(s => s.Id)
            .ToListAsync();

        return await dbContext.PendingDispatches.AsNoTracking()
            .Where(d => sessionIds.Contains(d.SessionId))
            .ToListAsync();
    }

    private async Task<List<InboxMessage>> GetMessagesAsync(Guid channelId, string externalId)
    {
        using var scope = fixture.InboxFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InboxAppDbContext>();
        var contact = await dbContext.Contacts.AsNoTracking().SingleAsync(c => c.ChannelId == channelId && c.ExternalId == externalId);
        var sessionIds = await dbContext.Sessions.AsNoTracking().Where(s => s.ContactId == contact.Id).Select(s => s.Id).ToListAsync();
        return await dbContext.Messages.AsNoTracking().Where(m => sessionIds.Contains(m.SessionId)).ToListAsync();
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
