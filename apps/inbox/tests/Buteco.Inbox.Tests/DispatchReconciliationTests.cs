using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using A2A;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Channels.Security;
using Buteco.Inbox.Contacts.Entities;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Messages.Entities;
using Buteco.Inbox.Orchestration;
using Buteco.Inbox.Orchestration.Entities;
using Buteco.Inbox.Orchestration.PushNotifications.Endpoints;
using Buteco.Inbox.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MessageEntity = Buteco.Inbox.Messages.Entities.Message;
using TaskStatus = A2A.TaskStatus;

namespace Buteco.Inbox.Tests;

/// <summary>
/// Reconciliação de PendingDispatch em Dispatching e aviso de falha à conversa
/// (#47). Cada linha é semeada num canal próprio, e as asserções olham só o
/// contato dela: as linhas dos testes anteriores continuam na mesma base.
/// </summary>
public class DispatchReconciliationTests : IClassFixture<ReconciliationFactoryFixture>
{
    // Texto fixado pelo dono (design.md, D6). Literal de propósito: o guarda tem
    // de reprovar contra o código anterior, que não tem a constante.
    private const string FailureNotice = "Não consegui responder agora. Pode tentar de novo em instantes?";

    private readonly ReconciliationFactoryFixture _factory;
    private readonly ConcurrentDictionary<string, Func<CancellationToken, Task<AgentTask>>> _tasks = new();

    public DispatchReconciliationTests(ReconciliationFactoryFixture factory)
    {
        _factory = factory;
        _factory.A2AClientFactory.Handler = FakeA2AClientFactory.DefaultHandler;
        _factory.A2AClientFactory.BeforeReturn = null;
        _factory.A2AClientFactory.GetTaskHandler = (request, token) =>
            _tasks.TryGetValue(request.Id, out var produce) ? produce(token) : FakeA2AClientFactory.TaskNotFoundHandler(request, token);
        _factory.GatedSender.ExceptionToThrow = null;
        _factory.GatedSender.Release();
    }

    // ── Linha com TaskId: decide o estado terminal (D3) ──────────────────────

    [Fact]
    public async Task Tracked_CompletedWithText_PastGrace_DeliversResponseAndRemoves()
    {
        var row = await SeedDispatchingAsync(taskId: NewId());
        _tasks[row.TaskId!] = _ => Task.FromResult(AgentTaskOf(row.TaskId!, TaskState.Completed, Ago(10), "Resposta atrasada."));

        await WaitUntilAsync(async () => await FindDispatchAsync(row.SessionId) is null, TimeSpan.FromSeconds(10));

        Assert.Equal("Resposta atrasada.", Assert.Single(DeliveredTo(row.ExternalId)).ResponseText);
        var messages = await GetMessagesAsync(row.SessionId);
        Assert.Equal(MessageDispatchStatus.Completed, Assert.Single(messages, m => m.Direction == MessageDirection.Inbound).DispatchStatus);
        var outbound = Assert.Single(messages, m => m.Direction == MessageDirection.Outbound);
        Assert.Equal("Resposta atrasada.", outbound.Content);
        Assert.Equal(MessageDeliveryStatus.Sent, outbound.DeliveryStatus);
    }

    [Fact]
    public async Task Tracked_Failed_PastGrace_DeliversNoticeAndMarksFailed()
    {
        var row = await SeedDispatchingAsync(taskId: NewId());
        _tasks[row.TaskId!] = _ => Task.FromResult(AgentTaskOf(row.TaskId!, TaskState.Failed, Ago(10)));

        await WaitUntilAsync(async () => await FindDispatchAsync(row.SessionId) is null, TimeSpan.FromSeconds(10));

        Assert.Equal(FailureNotice, Assert.Single(DeliveredTo(row.ExternalId)).ResponseText);
        var messages = await GetMessagesAsync(row.SessionId);
        Assert.Equal(MessageDispatchStatus.Failed, Assert.Single(messages, m => m.Direction == MessageDirection.Inbound).DispatchStatus);
        Assert.Equal(FailureNotice, Assert.Single(messages, m => m.Direction == MessageDirection.Outbound).Content);
    }

    [Fact]
    public async Task Tracked_TerminalWithinGrace_IsNotTouched_UntilTheGracePasses()
    {
        var row = await SeedDispatchingAsync(taskId: NewId());
        var terminalAt = DateTimeOffset.UtcNow;
        _tasks[row.TaskId!] = _ => Task.FromResult(AgentTaskOf(row.TaskId!, TaskState.Completed, terminalAt, "Dentro da carência."));

        // Âncora: a reconciliação JÁ consultou a task, e mais de uma vez.
        await WaitUntilAsync(() => Task.FromResult(GetTaskCalls(row.TaskId!) >= 3), TimeSpan.FromSeconds(10));
        Assert.True(DateTimeOffset.UtcNow - terminalAt < ReconciliationFactoryFixture.TerminalGrace);
        Assert.NotNull(await FindDispatchAsync(row.SessionId));
        Assert.Empty(DeliveredTo(row.ExternalId));

        await WaitUntilAsync(async () => await FindDispatchAsync(row.SessionId) is null, TimeSpan.FromSeconds(10));
        Assert.True(DateTimeOffset.UtcNow - terminalAt >= ReconciliationFactoryFixture.TerminalGrace);
        Assert.Single(DeliveredTo(row.ExternalId));
    }

    [Fact]
    public async Task Tracked_NonTerminal_IsNeverTouched_HoweverOld()
    {
        var row = await SeedDispatchingAsync(taskId: NewId(), lastMessageAt: DateTimeOffset.UtcNow.AddHours(-1));
        _tasks[row.TaskId!] = _ => Task.FromResult(AgentTaskOf(row.TaskId!, TaskState.Working, Ago(3600)));

        await WaitUntilAsync(() => Task.FromResult(GetTaskCalls(row.TaskId!) >= 5), TimeSpan.FromSeconds(10));

        var dispatch = await FindDispatchAsync(row.SessionId);
        Assert.NotNull(dispatch);
        Assert.Equal(PendingDispatchStatus.Dispatching, dispatch.Status);
        Assert.Equal(row.Token, dispatch.ExpectedToken);
        Assert.Empty(DeliveredTo(row.ExternalId));
    }

    [Fact]
    public async Task Tracked_NotFoundAndFailingQuery_LeaveTheirRows_AndDoNotBlockTheNext()
    {
        var notFound = await SeedDispatchingAsync(taskId: NewId());
        var failing = await SeedDispatchingAsync(taskId: NewId());
        var completed = await SeedDispatchingAsync(taskId: NewId());
        _tasks[failing.TaskId!] = _ => throw new HttpRequestException("apps/api fora do ar");
        _tasks[completed.TaskId!] = _ => Task.FromResult(AgentTaskOf(completed.TaskId!, TaskState.Completed, Ago(10), "ok"));

        await WaitUntilAsync(async () => await FindDispatchAsync(completed.SessionId) is null, TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() => Task.FromResult(GetTaskCalls(notFound.TaskId!) >= 2 && GetTaskCalls(failing.TaskId!) >= 2), TimeSpan.FromSeconds(10));

        Assert.Equal(notFound.Token, (await FindDispatchAsync(notFound.SessionId))!.ExpectedToken);
        Assert.Equal(failing.Token, (await FindDispatchAsync(failing.SessionId))!.ExpectedToken);
        Assert.Empty(DeliveredTo(notFound.ExternalId));
        Assert.Empty(DeliveredTo(failing.ExternalId));
        Assert.Contains(_factory.Logs, l => l.Level == LogLevel.Warning && l.Message.Contains(notFound.TaskId!));
        Assert.Contains(_factory.Logs, l => l.Level == LogLevel.Warning && l.Message.Contains(failing.TaskId!));
    }

    [Fact]
    public async Task Tracked_TerminalWithoutTimestamp_WaitsTheGraceFromTheFirstObservation()
    {
        var row = await SeedDispatchingAsync(taskId: NewId());
        var firstCall = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tasks[row.TaskId!] = _ =>
        {
            firstCall.TrySetResult(DateTimeOffset.UtcNow);
            return Task.FromResult(AgentTaskOf(row.TaskId!, TaskState.Failed, terminalAt: null));
        };

        var firstSeen = await firstCall.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() => Task.FromResult(GetTaskCalls(row.TaskId!) >= 3), TimeSpan.FromSeconds(10));
        Assert.NotNull(await FindDispatchAsync(row.SessionId));
        Assert.Empty(DeliveredTo(row.ExternalId));

        await WaitUntilAsync(async () => await FindDispatchAsync(row.SessionId) is null, TimeSpan.FromSeconds(10));
        Assert.True(DateTimeOffset.UtcNow - firstSeen >= ReconciliationFactoryFixture.TerminalGrace);
        Assert.Equal(FailureNotice, Assert.Single(DeliveredTo(row.ExternalId)).ResponseText);
    }

    [Fact]
    public async Task Tracked_PushArrivingAfterTheClaim_IsRejected_AndDeliveryHappensOnce()
    {
        var row = await SeedDispatchingAsync(taskId: NewId());
        var task = AgentTaskOf(row.TaskId!, TaskState.Completed, Ago(10), "Uma vez só.");
        _factory.GatedSender.Close();
        _tasks[row.TaskId!] = _ => Task.FromResult(task);

        await _factory.GatedSender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Com prazo: se o push fosse aceito, ele tentaria entregar pelo mesmo
        // sender, que está fechado, e só responderia depois de o teste abri-lo.
        var push = PostPushAsync(task, row.Token);
        var answeredWhileTheDeliveryWasHeld = await Task.WhenAny(push, Task.Delay(TimeSpan.FromSeconds(3))) == push;
        _factory.GatedSender.Release();
        var pushStatus = await push;

        await WaitUntilAsync(async () => await FindDispatchAsync(row.SessionId) is null, TimeSpan.FromSeconds(10));
        Assert.True(answeredWhileTheDeliveryWasHeld, "o push só respondeu depois de a entrega da reconciliação ser liberada");
        Assert.Equal(HttpStatusCode.Unauthorized, pushStatus);
        Assert.Single(DeliveredTo(row.ExternalId));
    }

    [Fact]
    public async Task Tracked_TwoInstancesReconcilingTheSameRow_DeliverOnce()
    {
        var row = await SeedDispatchingAsync(taskId: NewId());
        var task = AgentTaskOf(row.TaskId!, TaskState.Completed, Ago(10), "Duas instâncias, uma entrega.");
        var bothAsked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var asked = 0;

        // As duas instâncias só recebem a task depois de AMBAS terem perguntado:
        // as duas leem a linha antes de qualquer reivindicação, e só o xmin
        // decide quem entrega.
        _tasks[row.TaskId!] = async token =>
        {
            if (Interlocked.Increment(ref asked) >= 2)
            {
                bothAsked.TrySetResult();
            }

            await bothAsked.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            return task;
        };

        // A primeira entrega fica presa no sender: a linha continua existindo
        // enquanto a outra instância chega à reivindicação. Sem isso, a segunda
        // só lia a linha depois de a primeira removê-la, e o guarda passava com a
        // reivindicação revertida (medido na segunda perna, convenção 15).
        _factory.GatedSender.Close();
        var second = ActivatorUtilities.CreateInstance<DispatchReconciliationService>(_factory.Services);
        await second.StartAsync(CancellationToken.None);
        try
        {
            await _factory.GatedSender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Task.Delay(TimeSpan.FromSeconds(1));
            _factory.GatedSender.Release();
            await WaitUntilAsync(async () => await FindDispatchAsync(row.SessionId) is null, TimeSpan.FromSeconds(15));
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
        finally
        {
            await second.StopAsync(CancellationToken.None);
        }

        Assert.True(asked >= 2);
        Assert.Single(DeliveredTo(row.ExternalId));
    }

    [Fact]
    public async Task Tracked_ClaimedRowWhoseLeaseExpired_IsReclaimedAndResolved()
    {
        // Reivindicada há mais tempo que o prazo de posse (2 min padrão) e ainda
        // em Dispatching: quem a reivindicou parou no meio da entrega (D4).
        var row = await SeedDispatchingAsync(taskId: NewId(), claimedAt: DateTimeOffset.UtcNow.AddMinutes(-3));
        _tasks[row.TaskId!] = _ => Task.FromResult(AgentTaskOf(row.TaskId!, TaskState.Completed, Ago(600), "Depois da posse vencida."));

        await WaitUntilAsync(async () => await FindDispatchAsync(row.SessionId) is null, TimeSpan.FromSeconds(10));
        Assert.Single(DeliveredTo(row.ExternalId));
    }

    [Fact]
    public async Task Tracked_ClaimedRowWithinTheLease_IsNotTouched()
    {
        var row = await SeedDispatchingAsync(taskId: NewId(), claimedAt: DateTimeOffset.UtcNow);
        _tasks[row.TaskId!] = _ => Task.FromResult(AgentTaskOf(row.TaskId!, TaskState.Completed, Ago(600), "Em posse de outra instância."));

        await Task.Delay(TimeSpan.FromSeconds(1.5));

        Assert.NotNull(await FindDispatchAsync(row.SessionId));
        Assert.Empty(DeliveredTo(row.ExternalId));
        Assert.Equal(0, GetTaskCalls(row.TaskId!));
    }

    [Fact]
    public async Task Processor_LosingTheRowAtTheFinalSave_ReturnsAlreadyResolved_WithAWarning()
    {
        // Linha sem TaskId e recente: nenhuma regra da reconciliação a toca.
        var row = await SeedDispatchingAsync(taskId: null);
        var task = AgentTaskOf("task-do-push", TaskState.Completed, Ago(10), "Resposta do push.");

        using var pushScope = _factory.Services.CreateScope();
        var pushDb = pushScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seenByPush = await pushDb.PendingDispatches.SingleAsync(d => d.SessionId == row.SessionId);

        // A reconciliação reivindica entre a leitura do push e a gravação dele.
        using (var reconcilerScope = _factory.Services.CreateScope())
        {
            var reconcilerDb = reconcilerScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seenByReconciler = await reconcilerDb.PendingDispatches.SingleAsync(d => d.SessionId == row.SessionId);
            seenByReconciler.ClaimForReconciliation(DateTimeOffset.UtcNow);
            await reconcilerDb.SaveChangesAsync();
        }

        var processor = pushScope.ServiceProvider.GetRequiredService<DispatchOutcomeProcessor>();
        var outcome = await processor.CompleteFromTaskAsync(seenByPush, task, CancellationToken.None);

        Assert.Equal(DispatchOutcome.AlreadyResolved, outcome);
        Assert.NotNull(await FindDispatchAsync(row.SessionId));
        Assert.Contains(_factory.Logs, l =>
            l.Level == LogLevel.Warning && l.Category == typeof(DispatchOutcomeProcessor).FullName && l.Message.Contains(row.SessionId.ToString()));
        Assert.DoesNotContain(_factory.Logs, l =>
            l.Level >= LogLevel.Error && l.Category == typeof(DispatchOutcomeProcessor).FullName && l.Message.Contains(row.SessionId.ToString()));
    }

    // ── Linha sem TaskId: encerramento por idade (D7) ───────────────────────

    [Fact]
    public async Task Untracked_PastMaxAge_IsClosedAsLoss_WithNotice()
    {
        var row = await SeedDispatchingAsync(taskId: null, lastMessageAt: DateTimeOffset.UtcNow.AddHours(-1));

        await WaitUntilAsync(async () => await FindDispatchAsync(row.SessionId) is null, TimeSpan.FromSeconds(10));

        Assert.Equal(FailureNotice, Assert.Single(DeliveredTo(row.ExternalId)).ResponseText);
        var messages = await GetMessagesAsync(row.SessionId);
        Assert.Equal(MessageDispatchStatus.Failed, Assert.Single(messages, m => m.Direction == MessageDirection.Inbound).DispatchStatus);
        Assert.Contains(_factory.Logs, l => l.Level == LogLevel.Error && l.Message.Contains(row.SessionId.ToString()));
    }

    [Fact]
    public async Task Untracked_WithinMaxAge_IsNotTouched()
    {
        var row = await SeedDispatchingAsync(taskId: null, lastMessageAt: DateTimeOffset.UtcNow);

        // Bem dentro do limite de 4 s, e mais de dez ciclos da reconciliação.
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        var dispatch = await FindDispatchAsync(row.SessionId);
        Assert.NotNull(dispatch);
        Assert.Equal(row.Token, dispatch.ExpectedToken);
        Assert.Empty(DeliveredTo(row.ExternalId));
    }

    // ── As fontes 1 e 4, pelo caminho real do debounce ──────────────────────

    [Fact]
    public async Task Source4_PushArrivingBeforeTheTaskIdIsSaved_IsRecoveredByReconciliation()
    {
        HttpStatusCode? earlyPushStatus = null;
        _factory.A2AClientFactory.BeforeReturn = async (request, response, _) =>
        {
            var finished = AgentTaskOf(response.Task!.Id, TaskState.Completed, DateTimeOffset.UtcNow, "Resposta rápida.");
            _tasks[finished.Id] = _ => Task.FromResult(finished);
            earlyPushStatus = await PostPushAsync(finished, request.Configuration!.PushNotificationConfig!.Token!);
        };

        var (sessionId, externalId) = await ReceiveAsync();

        await WaitUntilAsync(async () => DeliveredTo(externalId).Count == 1 && await FindDispatchAsync(sessionId) is null, TimeSpan.FromSeconds(15));
        Assert.Equal(HttpStatusCode.Unauthorized, earlyPushStatus);
        Assert.Equal("Resposta rápida.", DeliveredTo(externalId).Single().ResponseText);
    }

    [Fact]
    public async Task Source1_UnexpectedExceptionAfterTheClaim_IsClosedAsLossAfterTheMaxAge()
    {
        _factory.A2AClientFactory.Handler = _ => throw new InvalidOperationException("Resposta inesperada do SendMessage.");
        var stopwatch = Stopwatch.StartNew();

        var (sessionId, externalId) = await ReceiveAsync();

        await WaitUntilAsync(async () => await FindDispatchAsync(sessionId) is null, TimeSpan.FromSeconds(15));
        Assert.True(stopwatch.Elapsed >= ReconciliationFactoryFixture.UntrackedDispatchMaxAge);
        Assert.Equal(FailureNotice, Assert.Single(DeliveredTo(externalId)).ResponseText);
        var inbound = Assert.Single(await GetMessagesAsync(sessionId), m => m.Direction == MessageDirection.Inbound);
        Assert.Equal(MessageDispatchStatus.Failed, inbound.DispatchStatus);
    }

    // ── Aviso de falha nos desfechos do debounce (D6) ───────────────────────

    [Fact]
    public async Task SyncRejection_DeliversTheNotice()
    {
        _factory.A2AClientFactory.Handler = FakeA2AClientFactory.RejectedHandler;

        var (sessionId, externalId) = await ReceiveAsync();

        await WaitUntilAsync(() => Task.FromResult(DeliveredTo(externalId).Count == 1), TimeSpan.FromSeconds(10));
        await AssertClosedAsFailureWithNoticeAsync(sessionId, externalId);
    }

    [Fact]
    public async Task ProtocolRejection_DeliversTheNotice()
    {
        _factory.A2AClientFactory.Handler = _ => throw new A2AException("Agente desconhecido.", A2AErrorCode.InvalidRequest);

        var (sessionId, externalId) = await ReceiveAsync();

        await WaitUntilAsync(() => Task.FromResult(DeliveredTo(externalId).Count == 1), TimeSpan.FromSeconds(10));
        await AssertClosedAsFailureWithNoticeAsync(sessionId, externalId);
    }

    [Fact]
    public async Task TransportExhaustion_DeliversTheNotice()
    {
        _factory.A2AClientFactory.Handler = _ => throw new HttpRequestException("apps/api inalcançável");

        var (sessionId, externalId) = await ReceiveAsync();

        await WaitUntilAsync(() => Task.FromResult(DeliveredTo(externalId).Count == 1), TimeSpan.FromSeconds(15));
        await AssertClosedAsFailureWithNoticeAsync(sessionId, externalId);
    }

    [Fact]
    public async Task TransportFailureStillRetryable_DoesNotDeliverTheNotice()
    {
        var attempts = 0;
        _factory.A2AClientFactory.Handler = request => Interlocked.Increment(ref attempts) == 1
            ? throw new HttpRequestException("falha passageira")
            : FakeA2AClientFactory.DefaultHandler(request);

        var (sessionId, externalId) = await ReceiveAsync();

        await WaitUntilAsync(async () => (await FindDispatchAsync(sessionId))?.TaskId is not null, TimeSpan.FromSeconds(10));
        Assert.Equal(2, attempts);
        Assert.Empty(DeliveredTo(externalId));
    }

    [Fact]
    public async Task NoticeDeliveryFails_IsPersistedAsFailed_AndTheRowIsRemoved()
    {
        _factory.A2AClientFactory.Handler = FakeA2AClientFactory.RejectedHandler;
        _factory.GatedSender.ExceptionToThrow = new HttpRequestException("canal fora do ar");

        var (sessionId, _) = await ReceiveAsync();

        await WaitUntilAsync(async () =>
            await FindDispatchAsync(sessionId) is null
            && (await GetMessagesAsync(sessionId)).Any(m => m.Direction == MessageDirection.Outbound), TimeSpan.FromSeconds(10));
        var outbound = Assert.Single(await GetMessagesAsync(sessionId), m => m.Direction == MessageDirection.Outbound);
        Assert.Equal(FailureNotice, outbound.Content);
        Assert.Equal(MessageDeliveryStatus.Failed, outbound.DeliveryStatus);
        Assert.Equal("canal fora do ar", outbound.DeliveryFailureReason);
    }

    // ── Apoio ────────────────────────────────────────────────────────────────

    private async Task AssertClosedAsFailureWithNoticeAsync(Guid sessionId, string externalId)
    {
        await WaitUntilAsync(async () => await FindDispatchAsync(sessionId) is null, TimeSpan.FromSeconds(10));
        Assert.Equal(FailureNotice, Assert.Single(DeliveredTo(externalId)).ResponseText);
        var messages = await GetMessagesAsync(sessionId);
        Assert.Equal(MessageDispatchStatus.Failed, Assert.Single(messages, m => m.Direction == MessageDirection.Inbound).DispatchStatus);
        var outbound = Assert.Single(messages, m => m.Direction == MessageDirection.Outbound);
        Assert.Equal(FailureNotice, outbound.Content);
        Assert.Equal(MessageDeliveryStatus.Sent, outbound.DeliveryStatus);
    }

    private sealed record SeededRow(Guid SessionId, string ExternalId, string? TaskId, string Token);

    private async Task<SeededRow> SeedDispatchingAsync(string? taskId, DateTimeOffset? lastMessageAt = null, DateTimeOffset? claimedAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cipher = scope.ServiceProvider.GetRequiredService<IChannelCredentialCipher>();

        var channel = new Channel(ReconciliationFactoryFixture.GatedChannelType, $"Canal {Guid.NewGuid()}", cipher.Encrypt("credencial"), Guid.NewGuid());
        dbContext.Channels.Add(channel);
        var externalId = $"+5511{Guid.NewGuid():N}"[..15];
        var contact = new Contact(channel.Id, externalId, new Dictionary<string, string>(), displayName: null);
        dbContext.Contacts.Add(contact);
        var session = new Session(contact.Id);
        dbContext.Sessions.Add(session);

        var dispatch = new PendingDispatch(session.Id, "Mensagem em voo", lastMessageAt ?? DateTimeOffset.UtcNow);
        var token = Guid.NewGuid().ToString("N");
        dispatch.MarkDispatching(token);
        if (taskId is not null)
        {
            dispatch.RegisterTaskId(taskId);
        }

        if (claimedAt is { } at)
        {
            dispatch.ClaimForReconciliation(at);
            token = dispatch.ExpectedToken!;
        }

        dbContext.PendingDispatches.Add(dispatch);
        dbContext.Messages.Add(MessageEntity.CreateInbound(
            session.Id, "Mensagem em voo", MessageContentType.Text, DateTimeOffset.UtcNow, Guid.NewGuid().ToString(), dispatch.Id));
        await dbContext.SaveChangesAsync();

        return new SeededRow(session.Id, externalId, taskId, token);
    }

    private async Task<(Guid SessionId, string ExternalId)> ReceiveAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cipher = scope.ServiceProvider.GetRequiredService<IChannelCredentialCipher>();
        var channel = new Channel(ReconciliationFactoryFixture.GatedChannelType, $"Canal {Guid.NewGuid()}", cipher.Encrypt("credencial"), Guid.NewGuid());
        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync();

        var externalId = $"+5511{Guid.NewGuid():N}"[..15];
        var orchestrator = scope.ServiceProvider.GetRequiredService<IInboundMessageOrchestrator>();
        await orchestrator.ReceiveMessageAsync(
            channel.Id, externalId, "Olá", MessageContentType.Text, Guid.NewGuid().ToString(),
            displayName: null, DateTimeOffset.UtcNow, new Dictionary<string, string>(), CancellationToken.None);

        var contact = await dbContext.Contacts.AsNoTracking().SingleAsync(c => c.ChannelId == channel.Id);
        var sessionId = await dbContext.Sessions.AsNoTracking().Where(s => s.ContactId == contact.Id).Select(s => s.Id).SingleAsync();
        return (sessionId, externalId);
    }

    private async Task<HttpStatusCode> PostPushAsync(AgentTask task, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PushNotificationEndpoints.RoutePattern)
        {
            Content = JsonContent.Create(task),
        };
        request.Headers.Add(PushNotificationEndpoints.TokenHeaderName, token);
        return (await _factory.CreateClient().SendAsync(request)).StatusCode;
    }

    private List<Buteco.Inbox.Channels.Adapters.OutboundMessage> DeliveredTo(string externalId) =>
        _factory.GatedSender.Delivered.Where(m => m.ContactExternalId == externalId).ToList();

    private int GetTaskCalls(string taskId) =>
        _factory.A2AClientFactory.GetTaskCalls.TryGetValue(taskId, out var count) ? count : 0;

    private async Task<PendingDispatch?> FindDispatchAsync(Guid sessionId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.PendingDispatches.AsNoTracking().SingleOrDefaultAsync(d => d.SessionId == sessionId);
    }

    private async Task<List<MessageEntity>> GetMessagesAsync(Guid sessionId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.Messages.AsNoTracking().Where(m => m.SessionId == sessionId).ToListAsync();
    }

    private static AgentTask AgentTaskOf(string id, TaskState state, DateTimeOffset? terminalAt, string? text = null) => new()
    {
        Id = id,
        ContextId = Guid.NewGuid().ToString("N"),
        Status = new TaskStatus { State = state, Timestamp = terminalAt },
        Artifacts = text is null ? null : [new Artifact { ArtifactId = Guid.NewGuid().ToString("N"), Parts = [Part.FromText(text)] }],
    };

    private static DateTimeOffset Ago(int seconds) => DateTimeOffset.UtcNow.AddSeconds(-seconds);

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!await condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condição não satisfeita a tempo.");
            }

            await Task.Delay(50);
        }
    }
}
