using System.Net;
using System.Net.Http.Json;
using A2A;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Contacts.Entities;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Messages.Entities;
using Buteco.Inbox.Orchestration;
using Buteco.Inbox.Orchestration.Entities;
using Buteco.Inbox.Orchestration.PushNotifications.Endpoints;
using Buteco.Inbox.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaskStatus = A2A.TaskStatus;

namespace Buteco.Inbox.Tests;

// Guardas 1, 2 e 3 da D5 da change inbox-serializacao-por-sessao (#162): o sweep
// não despacha uma sessão que tem linha em Dispatching (D1), e só a mesma
// sessão. OrchestrationFactoryFixture: janela de 300 ms, varredura de 50 ms, e
// FakeA2AClientFactory respondendo Submitted — a primeira linha fica em
// Dispatching até o push chegar pelo endpoint real.
public class DebounceSessionSerializationTests(OrchestrationFactoryFixture factory) : IClassFixture<OrchestrationFactoryFixture>
{
    // Janela + 2 varreduras, com folga para a contenção da VM: tempo bastante
    // para o sweep ter disparado uma Pending vencida, se fosse dispará-la.
    private static readonly TimeSpan BeyondWindow = TimeSpan.FromMilliseconds(900);

    /// <summary>
    /// Guarda 1 da D5. Com a primeira linha em <c>Dispatching</c>, duas mensagens que
    /// chegam fora da janela (e fora da janela uma da outra) não geram segundo
    /// <c>SendMessage</c>; depois da resolução pelo endpoint de push real sai
    /// <b>um</b> <c>SendMessage</c>, com as duas na ordem de chegada.
    /// </summary>
    /// <remarks>
    /// A ausência do segundo disparo é medida por tempo, e é a asserção fraca. O par
    /// determinístico é o conteúdo do lote: <c>"msg2\nmsg3"</c> num disparo só só
    /// acontece se msg2 esperou msg3, porque as duas chegam com mais de uma janela de
    /// distância (convenção 15, quinta forma). Contra o defeito, msg2 sai sozinha
    /// antes de msg3 chegar.
    /// </remarks>
    [Fact]
    public async Task SecondAndThirdMessages_WhileFirstIsDispatching_LeaveAsOneBatchAfterPushResolvesFirst()
    {
        factory.A2AClientFactory.Handler = FakeA2AClientFactory.DefaultHandler;

        var channelId = await CreateChannelAsync(Guid.NewGuid());
        var externalId = UniqueExternalId();

        await ReceiveAsync(channelId, externalId, "msg1");
        var sessionId = await ResolveSessionIdAsync(channelId, externalId);
        var contextId = await ResolveContextIdAsync(channelId, externalId);

        var first = await PollUntilAsync(
            () => GetDispatchesAsync(sessionId),
            dispatches => dispatches.Any(d => d is { Status: PendingDispatchStatus.Dispatching, TaskId: not null }),
            TimeSpan.FromSeconds(5));
        var firstDispatch = first.Single(d => d.Status == PendingDispatchStatus.Dispatching);

        await ReceiveAsync(channelId, externalId, "msg2");
        await Task.Delay(BeyondWindow);
        await ReceiveAsync(channelId, externalId, "msg3");
        await Task.Delay(BeyondWindow);

        var whileBusy = RequestTextsFor(contextId);
        Assert.Equal(["msg1"], whileBusy);

        var pending = Assert.Single(await GetDispatchesAsync(sessionId), d => d.Status == PendingDispatchStatus.Pending);
        Assert.Equal("msg2\nmsg3", pending.ConcatenatedText());

        var client = factory.CreateClient();
        var pushResponse = await PostPushNotificationAsync(
            client, firstDispatch.TaskId!, firstDispatch.ExpectedToken!, BuildCompletedTask(firstDispatch.TaskId!));
        Assert.Equal(HttpStatusCode.OK, pushResponse.StatusCode);

        await PollUntil(() => RequestTextsFor(contextId).Count, count => count >= 2, TimeSpan.FromSeconds(5));
        await Task.Delay(BeyondWindow);

        Assert.Equal(["msg1", "msg2\nmsg3"], RequestTextsFor(contextId));
    }

    /// <summary>
    /// Guarda 3 da D5. Com a sessão A em <c>Dispatching</c>, a <c>Pending</c> vencida
    /// da sessão B sai no ciclo.
    /// </summary>
    /// <remarks>
    /// <b>Verde no código anterior à change</b>, de propósito: é guarda de regressão
    /// contra um filtro global. Um <c>NOT EXISTS</c> que não amarrasse a
    /// <c>SessionId</c> passaria nos guardas 1 e 2 e serializaria o inbox inteiro; é
    /// este teste que reprova nesse caso.
    /// </remarks>
    [Fact]
    public async Task OtherSessionDispatching_DoesNotHoldExpiredPendingOfThisSession()
    {
        factory.A2AClientFactory.Handler = FakeA2AClientFactory.DefaultHandler;

        var channelId = await CreateChannelAsync(Guid.NewGuid());

        var externalIdA = UniqueExternalId();
        await ReceiveAsync(channelId, externalIdA, "sessão A");
        var sessionIdA = await ResolveSessionIdAsync(channelId, externalIdA);
        await PollUntilAsync(
            () => GetDispatchesAsync(sessionIdA),
            dispatches => dispatches.Any(d => d.Status == PendingDispatchStatus.Dispatching),
            TimeSpan.FromSeconds(5));

        var externalIdB = UniqueExternalId();
        await ReceiveAsync(channelId, externalIdB, "sessão B");
        var contextIdB = await ResolveContextIdAsync(channelId, externalIdB);

        await PollUntil(() => RequestTextsFor(contextIdB).Count, count => count >= 1, TimeSpan.FromSeconds(5));

        // A ainda está em Dispatching: B não esperou A ser resolvida.
        Assert.Contains(await GetDispatchesAsync(sessionIdA), d => d.Status == PendingDispatchStatus.Dispatching);
    }

    /// <summary>
    /// Guarda 2 da D5. Uma linha em <c>Dispatching</c> cujo <c>LastMessageAt</c> é mais
    /// velho que o teto (D3) não segura a <c>Pending</c> vencida da sessão, e não é
    /// alterada: continua em <c>Dispatching</c>, da reconciliação.
    /// </summary>
    /// <remarks>
    /// O teto é lido das opções em vigor (o default de produção, 10 min) e a linha é
    /// semeada 1 min além dele, em vez de encurtar o teto e esperar por ele: o guarda
    /// fica sem relógio, e exercita o valor que a produção usa. A linha leva
    /// <c>TaskId</c> para a reconciliação não a encerrar no meio do teste (o fake
    /// responde <c>TaskNotFound</c>, e a linha fica como está). Contra a remoção do
    /// teto, este reprova; o par é
    /// <see cref="DispatchingWithinMaxWait_StillHoldsExpiredPending"/>.
    /// </remarks>
    [Fact]
    public async Task DispatchingOlderThanMaxWait_NoLongerHoldsExpiredPending_AndIsLeftUntouched()
    {
        factory.A2AClientFactory.Handler = FakeA2AClientFactory.DefaultHandler;
        var maxWait = MaxWait();

        var (contextId, oldDispatchId) = await SeedSessionWithDispatchingAndExpiredPendingAsync(
            dispatchingLastMessageAt: DateTimeOffset.UtcNow - maxWait - TimeSpan.FromMinutes(1));

        await PollUntil(() => RequestTextsFor(contextId).Count, count => count >= 1, TimeSpan.FromSeconds(5));
        Assert.Equal(["pendente"], RequestTextsFor(contextId));

        var old = await GetDispatchAsync(oldDispatchId);
        Assert.NotNull(old);
        Assert.Equal(PendingDispatchStatus.Dispatching, old!.Status);
    }

    /// <summary>
    /// O par do guarda 2: a mesma linha, 1 min dentro do teto, segura a <c>Pending</c>.
    /// Sem ele, o teste acima passaria com um teto que nunca bloqueasse nada.
    /// </summary>
    [Fact]
    public async Task DispatchingWithinMaxWait_StillHoldsExpiredPending()
    {
        factory.A2AClientFactory.Handler = FakeA2AClientFactory.DefaultHandler;
        var maxWait = MaxWait();

        var (contextId, _) = await SeedSessionWithDispatchingAndExpiredPendingAsync(
            dispatchingLastMessageAt: DateTimeOffset.UtcNow - maxWait + TimeSpan.FromMinutes(1));

        await Task.Delay(BeyondWindow);

        Assert.Empty(RequestTextsFor(contextId));
    }

    private TimeSpan MaxWait() =>
        factory.Services.GetRequiredService<IOptions<DebounceOptions>>().Value.SessionSerializationMaxWait;

    private async Task<(string ContextId, Guid DispatchingId)> SeedSessionWithDispatchingAndExpiredPendingAsync(
        DateTimeOffset dispatchingLastMessageAt)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var channel = new Channel("test-channel", $"Canal {Guid.NewGuid()}", "irrelevante-nesta-fatia", Guid.NewGuid());
        dbContext.Channels.Add(channel);
        var contact = new Contact(channel.Id, UniqueExternalId(), new Dictionary<string, string>(), displayName: null);
        dbContext.Contacts.Add(contact);
        var session = new Session(contact.Id);
        dbContext.Sessions.Add(session);

        var dispatching = new PendingDispatch(session.Id, "em andamento", dispatchingLastMessageAt);
        dispatching.MarkDispatching(Guid.NewGuid().ToString("N"));
        dispatching.RegisterTaskId(Guid.NewGuid().ToString("N"));
        dbContext.PendingDispatches.Add(dispatching);

        // Janela já vencida no momento da gravação: o sweep a consideraria no primeiro
        // ciclo, se nada a segurasse.
        dbContext.PendingDispatches.Add(new PendingDispatch(session.Id, "pendente", DateTimeOffset.UtcNow - TimeSpan.FromSeconds(5)));

        await dbContext.SaveChangesAsync();
        return (session.ContextId, dispatching.Id);
    }

    private async Task<PendingDispatch?> GetDispatchAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.PendingDispatches.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id);
    }

    private List<string> RequestTextsFor(string contextId) =>
        factory.A2AClientFactory.Requests
            .Where(r => r.Message.ContextId == contextId)
            .OrderBy(r => r.Message.Metadata!["messageInstant"].GetString(), StringComparer.Ordinal)
            .Select(r => r.Message.Parts[0].Text!)
            .ToList();

    private async Task ReceiveAsync(Guid channelId, string externalId, string text)
    {
        using var scope = factory.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IInboundMessageOrchestrator>();
        await orchestrator.ReceiveMessageAsync(
            channelId,
            externalId,
            text,
            MessageContentType.Text,
            Guid.NewGuid().ToString(),
            displayName: null,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            CancellationToken.None);
    }

    private async Task<List<PendingDispatch>> GetDispatchesAsync(Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.PendingDispatches.AsNoTracking().Where(d => d.SessionId == sessionId).ToListAsync();
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

    private async Task<Guid> CreateChannelAsync(Guid agentId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var channel = new Channel("test-channel", $"Canal {Guid.NewGuid()}", "irrelevante-nesta-fatia", agentId);
        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync();
        return channel.Id;
    }

    private static Task<HttpResponseMessage> PostPushNotificationAsync(HttpClient client, string taskId, string token, AgentTask task)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, PushNotificationEndpoints.RoutePattern)
        {
            Content = JsonContent.Create(task),
        };
        request.Headers.Add(PushNotificationEndpoints.TokenHeaderName, token);
        return client.SendAsync(request);
    }

    private static AgentTask BuildCompletedTask(string taskId) => new()
    {
        Id = taskId,
        ContextId = Guid.NewGuid().ToString("N"),
        Status = new TaskStatus { State = TaskState.Completed, Timestamp = DateTimeOffset.UtcNow },
    };

    private static string UniqueExternalId() => $"+5511{Guid.NewGuid():N}"[..15];

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
}
