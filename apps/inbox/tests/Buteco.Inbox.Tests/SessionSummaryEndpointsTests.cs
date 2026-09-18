using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Contacts;
using Buteco.Inbox.Contacts.Responses;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Tests;

/// <summary>
/// <c>GET /sessions/summary</c> — contagem de sessões iniciadas num intervalo.
/// </summary>
/// <remarks>
/// CADA TESTE DESTA CLASSE USA UMA JANELA PRÓPRIA, DISTANTE E DISJUNTA (um ano
/// diferente, bem no passado), E ISSO NÃO É ESTILO — É CORREÇÃO.
/// A rota conta o sistema INTEIRO, e <see cref="InboxFactoryFixture"/> é
/// <c>IClassFixture</c>: um Postgres por classe, compartilhado por todos os
/// <c>[Fact]</c> dela. Sem janelas disjuntas, um teste contaria as sessões de
/// outro, e a reprovação apareceria de forma intermitente conforme a ordem de
/// execução. Os demais testes da suíte criam sessões em "agora", então nenhuma
/// janela no passado os alcança (design.md, Risks).
///
/// A retrodatação por SQL também é obrigatória, não conveniência:
/// <c>Session.StartedAt</c> é <c>private set</c>, atribuído no construtor
/// (<c>Session.cs:31</c>) — não há como escolher o instante pela API. Mesmo
/// mecanismo de <c>ContactSessionResolverTests.cs:57-62</c>.
///
/// Instantes sempre em segundos inteiros: <c>timestamptz</c> do Postgres guarda
/// microssegundos e <c>DateTimeOffset</c> guarda 100 ns, então um valor com
/// fração sub-microssegundo não voltaria idêntico — e os testes de limite
/// exatamente igual (4.1/4.2) dependem de igualdade exata.
/// </remarks>
public class SessionSummaryEndpointsTests(InboxFactoryFixture factory) : IClassFixture<InboxFactoryFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    // ---------------------------------------------------------------- 3.3/3.4

    [Fact]
    public async Task GetSessionSummary_SessionsInsideWindow_ReturnsStartedCount()
    {
        var channelId = await CreateChannelAsync();
        var (from, to) = WindowForYear(2001);

        await CreateSessionAsync(channelId, At(2001, 3, 10));
        await CreateSessionAsync(channelId, At(2001, 6, 20));
        await CreateSessionAsync(channelId, At(2001, 9, 20));

        var summary = await GetSummaryAsync(from, to);

        Assert.Equal(3, summary.StartedCount);
    }

    [Fact]
    public async Task GetSessionSummary_SessionsFromDifferentChannels_CountsAllInSameResponse()
    {
        var firstChannelId = await CreateChannelAsync();
        var secondChannelId = await CreateChannelAsync();
        var (from, to) = WindowForYear(2002);

        await CreateSessionAsync(firstChannelId, At(2002, 4, 1));
        await CreateSessionAsync(secondChannelId, At(2002, 8, 1));

        var summary = await GetSummaryAsync(from, to);

        Assert.Equal(2, summary.StartedCount);
    }

    // ------------------------------------------------------- 4.1-4.7 (bordas)

    [Fact]
    public async Task GetSessionSummary_SessionStartedExactlyAtFrom_IsCounted()
    {
        var channelId = await CreateChannelAsync();
        var (from, to) = WindowForYear(2003);

        await CreateSessionAsync(channelId, from);

        var summary = await GetSummaryAsync(from, to);

        Assert.Equal(1, summary.StartedCount);
    }

    [Fact]
    public async Task GetSessionSummary_SessionStartedExactlyAtTo_IsCounted()
    {
        var channelId = await CreateChannelAsync();
        var (from, to) = WindowForYear(2004);

        await CreateSessionAsync(channelId, to);

        var summary = await GetSummaryAsync(from, to);

        Assert.Equal(1, summary.StartedCount);
    }

    [Fact]
    public async Task GetSessionSummary_SessionStartedBeforeFrom_IsNotCounted()
    {
        var channelId = await CreateChannelAsync();
        var (from, to) = WindowForYear(2005);

        await CreateSessionAsync(channelId, from.AddSeconds(-1));

        var summary = await GetSummaryAsync(from, to);

        Assert.Equal(0, summary.StartedCount);
    }

    [Fact]
    public async Task GetSessionSummary_SessionStartedAfterTo_IsNotCounted()
    {
        var channelId = await CreateChannelAsync();
        var (from, to) = WindowForYear(2006);

        await CreateSessionAsync(channelId, to.AddSeconds(1));

        var summary = await GetSummaryAsync(from, to);

        Assert.Equal(0, summary.StartedCount);
    }

    // Trava a definição escolhida em D3: sem este teste, trocar StartedAt por
    // LastActivityAt no handler deixa a suíte inteira verde.
    [Fact]
    public async Task GetSessionSummary_SessionStartedBeforeWindowWithActivityInside_IsNotCounted()
    {
        var channelId = await CreateChannelAsync();
        var (from, to) = WindowForYear(2007);

        await CreateSessionAsync(channelId, from.AddDays(-30), lastActivityAt: At(2007, 5, 15));

        var summary = await GetSummaryAsync(from, to);

        Assert.Equal(0, summary.StartedCount);
    }

    // Trava a outra definição descartada (overlap), pelo mesmo motivo: uma sessão
    // iniciada antes da janela e nunca encerrada tem ClosedAt nulo — que NÃO
    // significa "aberta" (02-HISTORICO_E_STATUS.md, "Encerramento explícito de
    // sessão"). Pela definição de overlap ela contaria; pela escolhida, não.
    [Fact]
    public async Task GetSessionSummary_SessionStartedBeforeWindowAndNeverClosed_IsNotCounted()
    {
        var channelId = await CreateChannelAsync();
        var (from, to) = WindowForYear(2008);

        var sessionId = await CreateSessionAsync(channelId, from.AddDays(-60));

        Assert.Null(await ClosedAtOfAsync(sessionId));

        var summary = await GetSummaryAsync(from, to);

        Assert.Equal(0, summary.StartedCount);
    }

    // O zero é contagem MEDIDA, não falha nem ausência de recurso (convenção 13).
    // A asserção é sobre o PAR (status, valor): só o valor deixaria passar um 404
    // desserializado como zero.
    [Fact]
    public async Task GetSessionSummary_WindowWithoutSessions_ReturnsZeroWithOk()
    {
        var (from, to) = WindowForYear(2009);

        var response = await _client.GetAsync(SummaryUrl(from.ToString("O"), to.ToString("O")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<SessionPeriodSummaryResponse>();
        Assert.NotNull(summary);
        Assert.Equal(0, summary!.StartedCount);
    }

    // ---------------------------------------------------- 5.1-5.9 (validação)

    [Fact]
    public async Task GetSessionSummary_MissingFrom_ReturnsValidationProblemForFrom()
    {
        var (_, to) = WindowForYear(2012);

        var response = await _client.GetAsync(SummaryUrl(from: null, to: to.ToString("O")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["from"], await ErrorKeysOfAsync(response));
    }

    [Fact]
    public async Task GetSessionSummary_MissingTo_ReturnsValidationProblemForTo()
    {
        var (from, _) = WindowForYear(2012);

        var response = await _client.GetAsync(SummaryUrl(from: from.ToString("O"), to: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["to"], await ErrorKeysOfAsync(response));
    }

    // Sem este [Fact], uma implementação que retorna no PRIMEIRO defeito passa
    // 5.1 e 5.2 e continua errada.
    [Fact]
    public async Task GetSessionSummary_MissingBothBounds_ReportsBothInSameResponse()
    {
        var response = await _client.GetAsync(SummaryUrl(from: null, to: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["from", "to"], await ErrorKeysOfAsync(response));
    }

    [Fact]
    public async Task GetSessionSummary_MalformedFrom_ReturnsValidationProblemForFrom()
    {
        var (_, to) = WindowForYear(2012);

        var response = await _client.GetAsync(SummaryUrl("banana", to.ToString("O")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["from"], await ErrorKeysOfAsync(response));
    }

    // É ESTE [Fact] QUE JUSTIFICA O BINDING MANUAL DE D6: com DateTimeOffset? na
    // assinatura do endpoint, o valor malformado falha no binding e o ASP.NET
    // devolve um corpo de forma diferente — este teste reprova, e é ele que
    // impede a "simplificação" de voltar ao binding automático.
    [Fact]
    public async Task GetSessionSummary_MalformedAndMissingBound_ShareTheSameBodyShape()
    {
        var (_, to) = WindowForYear(2012);

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
    public async Task GetSessionSummary_ToBeforeFrom_ReturnsValidationProblem()
    {
        var (from, to) = WindowForYear(2012);

        var response = await _client.GetAsync(SummaryUrl(to.ToString("O"), from.ToString("O")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["to"], await ErrorKeysOfAsync(response));
    }

    // Borda degenerada do intervalo: from == to seleciona as sessões iniciadas
    // exatamente naquele instante, pelos limites inclusivos.
    [Fact]
    public async Task GetSessionSummary_FromEqualToTo_ReturnsOkCountingThatInstant()
    {
        var channelId = await CreateChannelAsync();
        var instant = At(2010, 7, 4);

        await CreateSessionAsync(channelId, instant);
        await CreateSessionAsync(channelId, instant.AddSeconds(1));

        var summary = await GetSummaryAsync(instant, instant);

        Assert.Equal(1, summary.StartedCount);
    }

    // Trava a ORDEM das duas checagens: o parse vem antes da comparação, então
    // um limite malformado é reportado como malformado mesmo quando o outro
    // limite faria o intervalo parecer invertido.
    [Fact]
    public async Task GetSessionSummary_MalformedBound_IsReportedAsMalformedNotInverted()
    {
        var (from, _) = WindowForYear(2012);

        var response = await _client.GetAsync(SummaryUrl(from.ToString("O"), "banana"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["to"], await ErrorKeysOfAsync(response));
        Assert.Contains("ISO 8601", await ErrorMessageOfAsync(response, "to"));
    }

    // AssumeUniversal decide qual instante um valor SEM deslocamento significa.
    // Sem ele, o valor recebe o offset local do processo — e, num servidor cujo
    // TZ não seja UTC, o Npgsql RECUSA o DateTimeOffset resultante ("only offset
    // 0 (UTC) is supported"), devolvendo 500. Verificado por mutação: removendo
    // os dois estilos, este é o único [Fact] que reprova (design.md, D6).
    [Fact]
    public async Task GetSessionSummary_BoundsWithoutOffset_AreInterpretedAsUtc()
    {
        var channelId = await CreateChannelAsync();
        var (from, to) = WindowForYear(2011);

        await CreateSessionAsync(channelId, At(2011, 5, 2));

        var withoutOffset = await GetSummaryAsync("2011-03-01T00:00:00", "2011-09-30T23:59:59");
        var withExplicitUtc = await GetSummaryAsync(from.ToString("O"), to.ToString("O"));

        Assert.Equal(withExplicitUtc.StartedCount, withoutOffset.StartedCount);
        Assert.Equal(1, withoutOffset.StartedCount);
    }

    // O requisito de spec exige normalizar deslocamento EXPLÍCITO para UTC, e
    // nenhum outro [Fact] cobria esse caminho: os demais mandam "O" (que já sai
    // +00:00) ou valor nu. É AdjustToUniversal — não AssumeUniversal — que
    // sustenta este caso, e sem ele o Npgsql recusa o offset e a rota devolve 500.
    [Fact]
    public async Task GetSessionSummary_BoundsWithExplicitNonUtcOffset_AreNormalizedToUtc()
    {
        var channelId = await CreateChannelAsync();

        // 2013-03-01T00:00:00-03:00 == 2013-03-01T03:00:00Z, e a sessão está
        // entre os dois: se o deslocamento fosse ignorado, ela ficaria de fora.
        await CreateSessionAsync(channelId, new DateTimeOffset(2013, 3, 1, 1, 30, 0, TimeSpan.Zero));

        var summary = await GetSummaryAsync("2013-02-28T21:00:00-03:00", "2013-09-30T20:59:59-03:00");

        Assert.Equal(1, summary.StartedCount);
    }

    // --------------------------------------------------------------- helpers

    private static DateTimeOffset At(int year, int month, int day) =>
        new(year, month, day, 12, 0, 0, TimeSpan.Zero);

    // A JANELA É UMA FATIA NO MEIO DO ANO, NÃO O ANO INTEIRO, E A DIFERENÇA JÁ
    // REPROVOU DOIS TESTES. Com a janela cobrindo o ano todo, um dado que precisa
    // ficar FORA dela (from.AddSeconds(-1), to.AddSeconds(1)) atravessa a
    // fronteira do ano e cai dentro da janela do teste vizinho — a contagem é
    // global, então ele passa a ser contado lá. Com março-setembro, tudo que um
    // teste cria continua dentro do seu próprio ano, e a unicidade do ano basta
    // para isolar.
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
            ? "/sessions/summary"
            : $"/sessions/summary?{string.Join('&', parameters)}";
    }

    private Task<SessionPeriodSummaryResponse> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to) =>
        GetSummaryAsync(from.ToString("O"), to.ToString("O"));

    private async Task<SessionPeriodSummaryResponse> GetSummaryAsync(string from, string to)
    {
        var response = await _client.GetAsync(SummaryUrl(from, to));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<SessionPeriodSummaryResponse>();
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

    private async Task<Guid> CreateSessionAsync(
        Guid channelId,
        DateTimeOffset startedAt,
        DateTimeOffset? lastActivityAt = null)
    {
        using var scope = factory.Services.CreateScope();

        // Criada pelo caminho real (mesmo helper de ChannelSessionEndpointsTests),
        // e só depois retrodatada: StartedAt é private set.
        var resolver = scope.ServiceProvider.GetRequiredService<IContactSessionResolver>();
        var session = await resolver.FindOrCreateSessionAsync(
            channelId, UniqueExternalId(), new Dictionary<string, string>(), displayName: null, CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE sessions
             SET "StartedAt" = {startedAt}, "LastActivityAt" = {lastActivityAt ?? startedAt}
             WHERE "Id" = {session.Id}
             """);

        return session.Id;
    }

    private async Task<DateTimeOffset?> ClosedAtOfAsync(Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.Sessions
            .AsNoTracking()
            .Where(session => session.Id == sessionId)
            .Select(session => session.ClosedAt)
            .SingleAsync();
    }

    // Cada sessão nasce de um contato próprio: o índice único parcial só permite
    // uma sessão aberta por Contact, e reusar o externalId faria o resolver
    // encerrar a sessão anterior em vez de criar outra.
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
