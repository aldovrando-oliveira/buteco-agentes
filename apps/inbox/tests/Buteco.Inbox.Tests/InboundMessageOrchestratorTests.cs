using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Messages.Entities;
using Buteco.Inbox.Orchestration;
using Buteco.Inbox.Orchestration.Entities;
using Buteco.Inbox.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Tests;

// Usa InboxFactoryFixture (janela de debounce default, 10s) de propósito —
// estes testes só verificam o comportamento de bufferização em si (tasks.md
// 6.1), não precisam do DebounceSweepService disparando de verdade dentro
// da janela do teste.
public class InboundMessageOrchestratorTests(InboxFactoryFixture factory) : IClassFixture<InboxFactoryFixture>
{
    [Fact]
    public async Task ReceiveMessageAsync_FirstMessage_CreatesPendingDispatchWithSingleMessage()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();

        await ReceiveAsync(channelId, externalId, "Olá");

        var pendingDispatch = await FindPendingDispatchAsync(channelId, externalId);

        Assert.NotNull(pendingDispatch);
        Assert.Equal(PendingDispatchStatus.Pending, pendingDispatch.Status);
        Assert.Single(pendingDispatch.Messages);
        Assert.Equal("Olá", pendingDispatch.Messages[0].Text);
    }

    [Fact]
    public async Task ReceiveMessageAsync_SecondMessageBeforeDispatch_AppendsToSamePendingDispatch()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();

        await ReceiveAsync(channelId, externalId, "Primeira mensagem");
        await ReceiveAsync(channelId, externalId, "Segunda mensagem");

        var pendingDispatches = await AllPendingDispatchesAsync(channelId, externalId);
        Assert.Single(pendingDispatches);

        var pendingDispatch = pendingDispatches[0];
        Assert.Equal(2, pendingDispatch.Messages.Count);
        Assert.Equal("Primeira mensagem", pendingDispatch.Messages[0].Text);
        Assert.Equal("Segunda mensagem", pendingDispatch.Messages[1].Text);
    }

    [Fact]
    public async Task ReceiveMessageAsync_ConcurrentCallsSameSession_ResolveToSinglePendingDispatch()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();

        // Mesma corrida (e mesma mitigação) que
        // ContactSessionResolverTests.FindOrCreateSessionAsync_ConcurrentCallsSamePair_...
        // exercita para Contact — aqui é a PendingDispatch Pending por
        // Session que precisa colidir em vez de duplicar (design.md,
        // Riscos).
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => ReceiveAsync(channelId, externalId, $"Mensagem {i}")));

        var pendingDispatches = await AllPendingDispatchesAsync(channelId, externalId);
        Assert.Single(pendingDispatches);
        Assert.Equal(8, pendingDispatches[0].Messages.Count);

        // Nenhuma Message perdida ou órfã através do caminho de retry
        // detach+rebusca (design.md, Risks — teste dedicado de atomicidade
        // entre PendingDispatch e Message).
        var messages = await AllMessagesAsync(channelId, externalId);
        Assert.Equal(8, messages.Count);
        Assert.All(messages, m => Assert.Equal(pendingDispatches[0].Id, m.PendingDispatchId));
        Assert.All(messages, m => Assert.Equal(MessageDispatchStatus.Pending, m.DispatchStatus));
    }

    [Fact]
    public async Task ReceiveMessageAsync_ConcurrentCallsExistingPendingDispatch_AppendsAllMessagesWithoutLoss()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();

        // Pré-cria a PendingDispatch fora de qualquer corrida — diferente
        // de ReceiveMessageAsync_ConcurrentCallsSameSession_ResolveToSinglePendingDispatch,
        // que só exercita o caminho pós-violação-de-unicidade (todas as
        // chamadas competem na criação). Aqui, as chamadas concorrentes
        // sempre encontram a PendingDispatch já existente na primeira
        // leitura de FindPendingAsync, exercitando o caminho direto de
        // append (design.md, Diagnóstico — caminho identificado como
        // afetado pelo mesmo bug mas sem cobertura própria até este teste).
        await ReceiveAsync(channelId, externalId, "Mensagem inicial");

        await Task.WhenAll(Enumerable.Range(0, 8).Select(i => ReceiveAsync(channelId, externalId, $"Mensagem concorrente {i}")));

        var pendingDispatches = await AllPendingDispatchesAsync(channelId, externalId);
        Assert.Single(pendingDispatches);
        Assert.Equal(9, pendingDispatches[0].Messages.Count);

        var messages = await AllMessagesAsync(channelId, externalId);
        Assert.Equal(9, messages.Count);
        Assert.All(messages, m => Assert.Equal(pendingDispatches[0].Id, m.PendingDispatchId));
    }

    [Fact]
    public async Task ReceiveMessageAsync_SameExternalMessageIdTwice_DoesNotDuplicateMessageOrBufferedText()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();
        var externalMessageId = Guid.NewGuid().ToString();

        await ReceiveAsync(channelId, externalId, "Mensagem original", externalMessageId);
        await ReceiveAsync(channelId, externalId, "Reentrega do mesmo webhook", externalMessageId);

        var messages = await AllMessagesAsync(channelId, externalId);
        var message = Assert.Single(messages);
        Assert.Equal("Mensagem original", message.Content);

        var pendingDispatch = await FindPendingDispatchAsync(channelId, externalId);
        Assert.NotNull(pendingDispatch);
        Assert.Single(pendingDispatch!.Messages);
        Assert.Equal("Mensagem original", pendingDispatch.Messages[0].Text);
    }

    [Fact]
    public async Task ReceiveMessageAsync_ConcurrentCallsSameExternalMessageId_PersistsOnlyOneMessage()
    {
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();
        var externalMessageId = Guid.NewGuid().ToString();

        // Sessão pré-resolvida fora da corrida — isola o dedup de Message
        // (o que este teste prova) da concorrência de criação de Session em
        // si (ContactSessionResolver não tem índice único protegendo
        // Session como tem para Contact/PendingDispatch — gap pré-existente
        // de inbox-crm-contato-sessao, fora do escopo desta change; sem
        // isolar, este teste fica dependente de timing e intermitente).
        // Mesmo raciocínio de
        // ReceiveMessageAsync_ConcurrentCallsExistingPendingDispatch_AppendsAllMessagesWithoutLoss.
        await ReceiveAsync(channelId, externalId, "Mensagem inicial");

        // Reentrega quase simultânea do mesmo webhook — exercita o catch de
        // violação de unicidade dentro de TryAppendWithRetryAsync, não só o
        // caminho sequencial acima (design.md, Decisão 5).
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ReceiveAsync(channelId, externalId, "Mesma mensagem", externalMessageId)));

        var messages = await AllMessagesAsync(channelId, externalId);
        Assert.Equal(2, messages.Count);
        Assert.Single(messages, m => m.ExternalId == externalMessageId);
    }

    // externalMessageId único por chamada por padrão — as concorrentes
    // abaixo simulam N mensagens distintas, não N reentregas da mesma
    // (design.md, Decisão 5); testes de dedup passam o mesmo id de
    // propósito.
    private async Task ReceiveAsync(Guid channelId, string externalId, string text, string? externalMessageId = null)
    {
        using var scope = factory.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IInboundMessageOrchestrator>();
        await orchestrator.ReceiveMessageAsync(
            channelId,
            externalId,
            text,
            MessageContentType.Text,
            externalMessageId ?? Guid.NewGuid().ToString(),
            displayName: null,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            CancellationToken.None);
    }

    private async Task<PendingDispatch?> FindPendingDispatchAsync(Guid channelId, string externalId)
    {
        var all = await AllPendingDispatchesAsync(channelId, externalId);
        return all.SingleOrDefault();
    }

    private async Task<List<PendingDispatch>> AllPendingDispatchesAsync(Guid channelId, string externalId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var contact = await dbContext.Contacts.AsNoTracking()
            .SingleAsync(c => c.ChannelId == channelId && c.ExternalId == externalId);
        var sessionIds = await dbContext.Sessions.AsNoTracking()
            .Where(s => s.ContactId == contact.Id)
            .Select(s => s.Id)
            .ToListAsync();

        return await dbContext.PendingDispatches.AsNoTracking()
            .Where(d => sessionIds.Contains(d.SessionId))
            .ToListAsync();
    }

    private async Task<List<Message>> AllMessagesAsync(Guid channelId, string externalId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var contact = await dbContext.Contacts.AsNoTracking()
            .SingleAsync(c => c.ChannelId == channelId && c.ExternalId == externalId);
        var sessionIds = await dbContext.Sessions.AsNoTracking()
            .Where(s => s.ContactId == contact.Id)
            .Select(s => s.Id)
            .ToListAsync();

        return await dbContext.Messages.AsNoTracking()
            .Where(m => sessionIds.Contains(m.SessionId))
            .ToListAsync();
    }

    private static string UniqueExternalId() => $"+5511{Guid.NewGuid():N}"[..15];

    private async Task<Guid> CreateChannelAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var channel = new Channel("test-channel", $"Canal {Guid.NewGuid()}", "irrelevante-nesta-fatia", Guid.NewGuid());
        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync();
        return channel.Id;
    }
}
