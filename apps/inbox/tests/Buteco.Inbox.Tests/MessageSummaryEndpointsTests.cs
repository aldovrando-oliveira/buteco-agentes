using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Contacts;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Messages.Entities;
using Buteco.Inbox.Messages.Responses;
using Buteco.Inbox.Orchestration;
using Buteco.Inbox.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Tests;

/// <summary>
/// <c>GET /messages/summary</c> — contagem de mensagens de entrada por período.
/// </summary>
/// <remarks>
/// A rota conta o sistema INTEIRO e o banco é compartilhado por todos os testes
/// da classe, então cada caso usa uma JANELA PRÓPRIA, DISJUNTA E BEM NO PASSADO
/// (um ano por caso). Os demais testes da suíte criam mensagens em "agora", e
/// nenhuma janela no passado os alcança.
///
/// OS ANOS SÃO OS DE 1980-1993, E A ESCOLHA NÃO É ARBITRÁRIA: os anos 2001-2013
/// já estão tomados por <see cref="SessionSummaryEndpointsTests"/>. A colisão que
/// importa não é de mensagens — as duas rotas contam tabelas diferentes —, é do
/// caso 4.7, que RETRODATA UMA SESSÃO: uma sessão datada em 2001-2013 seria
/// contada por <c>GetSessionSummary_*</c> e reprovaria testes alheios a esta
/// change.
/// </remarks>
public class MessageSummaryEndpointsTests(InboxFactoryFixture factory) : IClassFixture<InboxFactoryFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    // ── 3.3-3.4 · caminho feliz e alcance da contagem ────────────────────────

    [Fact]
    public async Task GetMessageSummary_InboundMessagesInsideWindow_ReturnsInboundCount()
    {
        var (from, to) = WindowForYear(1980);
        var channelId = await CreateChannelAsync();

        await CreateInboundAtAsync(channelId, from.AddDays(10));
        await CreateInboundAtAsync(channelId, from.AddDays(20));
        await CreateInboundAtAsync(channelId, from.AddDays(30));

        var summary = await GetSummaryAsync(from, to);

        Assert.Equal(3, summary.InboundCount);
    }

    [Fact]
    public async Task GetMessageSummary_MessagesFromDifferentChannels_CountsAllInSameResponse()
    {
        var (from, to) = WindowForYear(1981);

        await CreateInboundAtAsync(await CreateChannelAsync(), from.AddDays(5));
        await CreateInboundAtAsync(await CreateChannelAsync(), from.AddDays(15));

        var summary = await GetSummaryAsync(from, to);

        // Nenhum canal, contato ou sessão foi informado na chamada.
        Assert.Equal(2, summary.InboundCount);
    }

    // ── 4.1-4.4 · bordas do intervalo ────────────────────────────────────────

    [Fact]
    public async Task GetMessageSummary_MessageOccurredExactlyAtFrom_IsCounted()
    {
        var (from, to) = WindowForYear(1982);

        await CreateInboundAtAsync(await CreateChannelAsync(), from);

        var summary = await GetSummaryAsync(from, to);

        Assert.Equal(1, summary.InboundCount);
    }

    [Fact]
    public async Task GetMessageSummary_MessageOccurredExactlyAtTo_IsCounted()
    {
        var (from, to) = WindowForYear(1983);

        await CreateInboundAtAsync(await CreateChannelAsync(), to);

        var summary = await GetSummaryAsync(from, to);

        Assert.Equal(1, summary.InboundCount);
    }

    [Fact]
    public async Task GetMessageSummary_MessageOccurredBeforeFrom_IsNotCounted()
    {
        var (from, to) = WindowForYear(1984);

        await CreateInboundAtAsync(await CreateChannelAsync(), from.AddSeconds(-1));

        var summary = await GetSummaryAsync(from, to);

        Assert.Equal(0, summary.InboundCount);
    }

    [Fact]
    public async Task GetMessageSummary_MessageOccurredAfterTo_IsNotCounted()
    {
        var (from, to) = WindowForYear(1985);

        await CreateInboundAtAsync(await CreateChannelAsync(), to.AddSeconds(1));

        var summary = await GetSummaryAsync(from, to);

        Assert.Equal(0, summary.InboundCount);
    }

    // ── 4.5 · O DISCRIMINADOR ────────────────────────────────────────────────

    // É ESTE [Fact] QUE TRAVA A DEFINIÇÃO ESCOLHIDA (design.md, D6), e é o
    // análogo do teste de LastActivityAt da change anterior. Sem ele, remover o
    // predicado de Direction do handler — ou trocá-lo por Outbound — deixa a
    // suíte verde e a rota passa a contar outra coisa.
    //
    // As duas de saída cobrem os DOIS estados de entrega (Sent e Failed), porque
    // o requisito diz "independentemente de seu estado de entrega": uma
    // implementação que filtrasse só Sent continuaria errada.
    [Fact]
    public async Task GetMessageSummary_OutboundMessagesInsideWindow_DoNotCountAsInbound()
    {
        var (from, to) = WindowForYear(1986);
        var channelId = await CreateChannelAsync();
        var sessionId = await ResolveSessionIdAsync(channelId, UniqueExternalId());

        await CreateOutboundAsync(sessionId, MessageDeliveryStatus.Sent, deliveryFailureReason: null);
        await CreateOutboundAsync(sessionId, MessageDeliveryStatus.Failed, "canal indisponível");
        await BackdateMessagesAsync(sessionId, from.AddDays(10));

        var summary = await GetSummaryAsync(from, to);

        Assert.Equal(0, summary.InboundCount);
    }

    // ── 4.6 · dedup ──────────────────────────────────────────────────────────

    // GUARDA DE REGRESSÃO SOBRE DEPENDÊNCIA, não cobertura de código novo: a
    // dedup é de inbox-mensagens-persistidas (InboundMessageOrchestrator.cs:47,67
    // mais o índice único parcial de AppDbContext.cs:168-170), e nada nesta
    // change a implementa. Existe porque o REQUISITO DE SPEC que ela sustenta é
    // desta change (design.md, D7): a unidade contada é a mensagem, não a entrega.
    [Fact]
    public async Task GetMessageSummary_RedeliveredWebhook_CountsOnce()
    {
        var (from, to) = WindowForYear(1987);
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();
        var externalMessageId = Guid.NewGuid().ToString();

        // Mesmo evento entregue duas vezes pelo provedor: mesmo externalMessageId,
        // mesma sessão. A segunda é deduplicada pelo caminho real.
        await ReceiveAsync(channelId, externalId, "Olá", externalMessageId);
        await ReceiveAsync(channelId, externalId, "Olá", externalMessageId);

        var sessionId = await ResolveSessionIdAsync(channelId, externalId);
        await BackdateMessagesAsync(sessionId, from.AddDays(10));

        var summary = await GetSummaryAsync(from, to);

        Assert.Equal(1, summary.InboundCount);
    }

    // ── 4.7 · espelho de /sessions/summary ───────────────────────────────────

    // TESTE DE CONTRATO, NÃO DE CÓDIGO — e está registrado como tal para a
    // próxima varredura não o levantar como redundante. Ele não exercita nenhum
    // caminho que o caso feliz já não exercite, e NENHUMA MUTAÇÃO O ISOLA: o
    // handler não olha para a sessão, que é exatamente o que ele afirma.
    //
    // Existe para que a diferença entre as duas rotas de período fique afirmada
    // em vez de subentendida — os números não se implicam —, e para reprovar caso
    // alguém "melhore" o handler juntando-o a Session.StartedAt.
    [Fact]
    public async Task GetMessageSummary_MessageInsideWindowOnSessionStartedBefore_IsCounted()
    {
        var (from, to) = WindowForYear(1988);
        var channelId = await CreateChannelAsync();
        var externalId = UniqueExternalId();

        // A ORDEM É RECEBER PRIMEIRO E RETRODATAR DEPOIS, E ISSO NÃO É ESTILO —
        // foi medido: a ordem inversa reprova este teste com 0 em vez de 1.
        // ContactSessionResolver.cs:57-58 fecha a sessão e abre outra quando
        // UtcNow - LastActivityAt passa de InactivityTimeout (1 h). Retrodatar a
        // sessão ANTES de receber faz a mensagem cair numa sessão NOVA, e o
        // UPDATE seguinte retrodata a sessão vazia.
        await ReceiveAsync(channelId, externalId, "Mensagem dentro da janela");
        var sessionId = await ResolveSessionIdAsync(channelId, externalId);

        await BackdateSessionAsync(sessionId, from.AddYears(-1));
        await BackdateMessagesAsync(sessionId, from.AddDays(10));

        var summary = await GetSummaryAsync(from, to);

        // A sessão começou um ano ANTES da janela; a mensagem caiu DENTRO dela.
        Assert.Equal(1, summary.InboundCount);
    }

    // ── 4.8 · zero é contagem medida ─────────────────────────────────────────

    [Fact]
    public async Task GetMessageSummary_WindowWithoutMessages_ReturnsZeroWithOk()
    {
        var (from, to) = WindowForYear(1989);

        var response = await _client.GetAsync(SummaryUrl(from.ToString("O"), to.ToString("O")));

        // O par (status, valor), não só o valor: o zero é contagem MEDIDA, não
        // ausência de recurso nem corpo vazio.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<MessagePeriodSummaryResponse>();
        Assert.NotNull(summary);
        Assert.Equal(0, summary!.InboundCount);
    }

    // ── 5.1-5.10 · validação ─────────────────────────────────────────────────

    [Fact]
    public async Task GetMessageSummary_MissingFrom_ReturnsValidationProblemForFrom()
    {
        var (_, to) = WindowForYear(1993);

        var response = await _client.GetAsync(SummaryUrl(from: null, to: to.ToString("O")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["from"], await ErrorKeysOfAsync(response));
    }

    [Fact]
    public async Task GetMessageSummary_MissingTo_ReturnsValidationProblemForTo()
    {
        var (from, _) = WindowForYear(1993);

        var response = await _client.GetAsync(SummaryUrl(from.ToString("O"), to: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["to"], await ErrorKeysOfAsync(response));
    }

    // Sem este [Fact], uma implementação que retorna no PRIMEIRO defeito passa os
    // dois acima e continua errada.
    [Fact]
    public async Task GetMessageSummary_MissingBothBounds_ReportsBothInSameResponse()
    {
        var response = await _client.GetAsync(SummaryUrl(from: null, to: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["from", "to"], await ErrorKeysOfAsync(response));
    }

    [Fact]
    public async Task GetMessageSummary_MalformedFrom_ReturnsValidationProblemForFrom()
    {
        var (_, to) = WindowForYear(1993);

        var response = await _client.GetAsync(SummaryUrl("banana", to.ToString("O")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["from"], await ErrorKeysOfAsync(response));
    }

    // É ESTE [Fact] QUE JUSTIFICA O BINDING MANUAL DE D8: com DateTimeOffset? na
    // assinatura do endpoint, o valor malformado falha no binding e o ASP.NET
    // devolve um corpo de forma diferente — este teste reprova, e é ele que
    // impede a "simplificação" de voltar ao binding automático.
    [Fact]
    public async Task GetMessageSummary_MalformedAndMissingBound_ShareTheSameBodyShape()
    {
        var (_, to) = WindowForYear(1993);

        var malformed = await _client.GetAsync(SummaryUrl("banana", to.ToString("O")));
        var missing = await _client.GetAsync(SummaryUrl(from: null, to: to.ToString("O")));

        Assert.Equal(malformed.StatusCode, missing.StatusCode);
        Assert.Equal(malformed.Content.Headers.ContentType?.MediaType, missing.Content.Headers.ContentType?.MediaType);
        Assert.Equal(await TopLevelPropertiesOfAsync(malformed), await TopLevelPropertiesOfAsync(missing));
        Assert.Equal(await ErrorKeysOfAsync(malformed), await ErrorKeysOfAsync(missing));

        // Mesma forma, mensagens diferentes — se fossem iguais, a rota estaria
        // escondendo qual dos dois defeitos ocorreu.
        Assert.NotEqual(await ErrorMessageOfAsync(malformed, "from"), await ErrorMessageOfAsync(missing, "from"));
    }

    [Fact]
    public async Task GetMessageSummary_ToBeforeFrom_ReturnsValidationProblem()
    {
        var (from, to) = WindowForYear(1993);

        var response = await _client.GetAsync(SummaryUrl(to.ToString("O"), from.ToString("O")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["to"], await ErrorKeysOfAsync(response));
    }

    [Fact]
    public async Task GetMessageSummary_FromEqualToTo_ReturnsOkCountingThatInstant()
    {
        var (from, _) = WindowForYear(1990);
        var instant = from.AddDays(10);

        await CreateInboundAtAsync(await CreateChannelAsync(), instant);

        // Borda degenerada do intervalo: pelos limites inclusivos, seleciona o
        // que ocorreu exatamente naquele instante.
        var summary = await GetSummaryAsync(instant, instant);

        Assert.Equal(1, summary.InboundCount);
    }

    // Trava a ORDEM das duas checagens: o parse dos dois limites vem antes da
    // comparação de ordem.
    [Fact]
    public async Task GetMessageSummary_MalformedBound_IsReportedAsMalformedNotInverted()
    {
        var (from, _) = WindowForYear(1993);

        var response = await _client.GetAsync(SummaryUrl(from.ToString("O"), "banana"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["to"], await ErrorKeysOfAsync(response));
        Assert.Contains("ISO 8601", await ErrorMessageOfAsync(response, "to"));
    }

    // O TESTE DO AssumeUniversal. Sem ele, o valor nu recebe o offset LOCAL do
    // processo e o Npgsql recusa o DateTimeOffset resultante — 500, não número
    // errado, e invisível num servidor com TZ=UTC (design.md, D8).
    [Fact]
    public async Task GetMessageSummary_BoundsWithoutOffset_AreInterpretedAsUtc()
    {
        var (from, to) = WindowForYear(1991);

        await CreateInboundAtAsync(await CreateChannelAsync(), from.AddDays(10));

        var bare = await GetSummaryAsync(
            from.ToString("yyyy-MM-ddTHH:mm:ss"), to.ToString("yyyy-MM-ddTHH:mm:ss"));
        var explicitUtc = await GetSummaryAsync(from, to);

        Assert.Equal(explicitUtc.InboundCount, bare.InboundCount);
        Assert.Equal(1, bare.InboundCount);
    }

    // O TESTE DO AdjustToUniversal, separado do anterior de propósito: na change
    // anterior ele FALTOU NO PLANO e só apareceu na implementação, porque os
    // demais testes mandam "O" (que já sai +00:00) ou valor nu, e nenhum cobria
    // o caminho do offset explícito não-UTC.
    [Fact]
    public async Task GetMessageSummary_BoundsWithExplicitNonUtcOffset_AreNormalizedToUtc()
    {
        var (from, to) = WindowForYear(1992);

        await CreateInboundAtAsync(await CreateChannelAsync(), from.AddDays(10));

        // ToOffset preserva o INSTANTE e troca só a representação.
        var shifted = await GetSummaryAsync(
            from.ToOffset(TimeSpan.FromHours(-3)).ToString("O"),
            to.ToOffset(TimeSpan.FromHours(-3)).ToString("O"));
        var utc = await GetSummaryAsync(from, to);

        Assert.Equal(utc.InboundCount, shifted.InboundCount);
        Assert.Equal(1, shifted.InboundCount);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    // A JANELA É UMA FATIA NO MEIO DO ANO, NÃO O ANO INTEIRO, e a razão está
    // escrita em SessionSummaryEndpointsTests: com a janela cobrindo o ano todo,
    // um dado que precisa ficar FORA dela (from.AddSeconds(-1), to.AddSeconds(1))
    // atravessa a fronteira do ano e cai dentro da janela do teste vizinho.
    private static (DateTimeOffset From, DateTimeOffset To) WindowForYear(int year) =>
        (new DateTimeOffset(year, 3, 1, 0, 0, 0, TimeSpan.Zero),
         new DateTimeOffset(year, 9, 30, 23, 59, 59, TimeSpan.Zero));

    private static string SummaryUrl(string? from, string? to)
    {
        var parameters = new List<string>();
        if (from is not null)
        {
            parameters.Add($"from={Uri.EscapeDataString(from)}");
        }

        if (to is not null)
        {
            parameters.Add($"to={Uri.EscapeDataString(to)}");
        }

        return parameters.Count == 0
            ? "/messages/summary"
            : $"/messages/summary?{string.Join('&', parameters)}";
    }

    private Task<MessagePeriodSummaryResponse> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to) =>
        GetSummaryAsync(from.ToString("O"), to.ToString("O"));

    private async Task<MessagePeriodSummaryResponse> GetSummaryAsync(string from, string to)
    {
        var response = await _client.GetAsync(SummaryUrl(from, to));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<MessagePeriodSummaryResponse>();
        Assert.NotNull(summary);
        return summary!;
    }

    private static async Task<string[]> ErrorKeysOfAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. document.RootElement.GetProperty("errors").EnumerateObject().Select(e => e.Name).Order()];
    }

    private static async Task<string[]> TopLevelPropertiesOfAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. document.RootElement.EnumerateObject().Select(p => p.Name).Order()];
    }

    private static async Task<string> ErrorMessageOfAsync(HttpResponseMessage response, string key)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("errors").GetProperty(key)[0].GetString()!;
    }

    // Cria pelo CAMINHO REAL e só então retrodata — a ordem importa duas vezes:
    // preserva a dedup do orquestrador, e mantém PendingDispatch.LastMessageAt em
    // "agora" (ver BackdateMessagesAsync).
    private async Task<Guid> CreateInboundAtAsync(Guid channelId, DateTimeOffset occurredAt)
    {
        var externalId = UniqueExternalId();
        await ReceiveAsync(channelId, externalId, "Mensagem de entrada");

        var sessionId = await ResolveSessionIdAsync(channelId, externalId);
        await BackdateMessagesAsync(sessionId, occurredAt);

        return sessionId;
    }

    // RETRODATAÇÃO POR UPDATE, NÃO POR receivedAt NO ORQUESTRADOR, E ISSO É
    // DECISÃO (tasks.md, 2.4). ReceiveMessageAsync aceita receivedAt como
    // parâmetro, então retrodatar por ali PARECE mais limpo. Mas o mesmo valor
    // alimenta PendingDispatch.LastMessageAt (InboundMessageOrchestrator.cs:59), e
    // DebounceSweepService roda de verdade nestes testes (Program.cs:115, hosted
    // service que a WebApplicationFactory inicia): com Window de 10 s e
    // SweepInterval de 2 s, uma mensagem datada de 1980 fica elegível no PRÓXIMO
    // sweep e o dispatch dispara no meio do teste.
    //
    // Criar em "agora" e retrodatar depois mantém LastMessageAt dentro da janela
    // de 10 s, e o sweep não alcança o teste. Mesmo mecanismo de
    // SessionSummaryEndpointsTests.cs e ContactSessionResolverTests.cs, pelo mesmo
    // motivo de fundo: OccurredAt é private set e não há como escolher o instante
    // pela API — que é justamente a propriedade de design.md, D3.
    private async Task BackdateMessagesAsync(Guid sessionId, DateTimeOffset occurredAt)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE messages
             SET "OccurredAt" = {occurredAt}
             WHERE "SessionId" = {sessionId}
             """);
    }

    private async Task BackdateSessionAsync(Guid sessionId, DateTimeOffset startedAt)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE sessions
             SET "StartedAt" = {startedAt}, "LastActivityAt" = {startedAt}
             WHERE "Id" = {sessionId}
             """);
    }

    // Inserida direto pelo AppDbContext, e não pelo ciclo real de push
    // notification. É o que MessagePersistenceTests.CreateOutboundAsync já faz
    // (mesmo mecanismo, mesma suíte): o que o teste afirma é o PREDICADO DO
    // HANDLER, não o caminho de gravação, que já tem cobertura própria lá.
    private async Task CreateOutboundAsync(
        Guid sessionId, MessageDeliveryStatus deliveryStatus, string? deliveryFailureReason)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Messages.Add(Message.CreateOutbound(
            sessionId, "Resposta do agente", DateTimeOffset.UtcNow, deliveryStatus, deliveryFailureReason));
        await dbContext.SaveChangesAsync();
    }

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

    private async Task<Guid> ResolveSessionIdAsync(Guid channelId, string externalId)
    {
        using var scope = factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IContactSessionResolver>();
        var session = await resolver.FindOrCreateSessionAsync(
            channelId, externalId, new Dictionary<string, string>(), displayName: null, CancellationToken.None);
        return session.Id;
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
