extern alias ApiAssembly;
extern alias InboxAssembly;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InboxOrchestratorRoundTrip.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using InboxAppDbContext = InboxAssembly::Buteco.Inbox.Infrastructure.AppDbContext;
using InboxChannel = InboxAssembly::Buteco.Inbox.Channels.Entities.Channel;
using InboxInboundMessageOrchestrator = InboxAssembly::Buteco.Inbox.Orchestration.IInboundMessageOrchestrator;
using InboxPendingDispatch = InboxAssembly::Buteco.Inbox.Orchestration.Entities.PendingDispatch;

namespace InboxOrchestratorRoundTrip.Tests;

/// <summary>
/// Prova direta do round-trip completo desta change (design.md, Decisão
/// 10; tasks.md 7.2): mensagem "chega" via chamada direta a
/// <c>IInboundMessageOrchestrator</c> → debounce dispara →
/// <c>SendMessage</c> real contra apps/api de teste → job processado por
/// apps/workers de teste (IChatClient mockado) → push notification real
/// recebida no endpoint próprio de apps/inbox, com token validado e
/// payload correto.
/// </summary>
public class RoundTripTests(RoundTripFixture fixture) : IClassFixture<RoundTripFixture>
{
    [Fact]
    public async Task MessageReceived_TriggersFullRoundTrip_PushNotificationReceivedWithCorrectPayload()
    {
        var agentId = await CreateAgentAsync();
        var (channelId, externalId) = await CreateChannelAsync(agentId);

        using (var scope = fixture.InboxFactory.Services.CreateScope())
        {
            var orchestrator = scope.ServiceProvider.GetRequiredService<InboxInboundMessageOrchestrator>();
            await orchestrator.ReceiveMessageAsync(channelId, externalId, "Olá, preciso de ajuda", DateTimeOffset.UtcNow, new Dictionary<string, string>(), CancellationToken.None);
        }

        // Debounce disparou o SendMessage real: a PendingDispatch fica
        // Dispatching com um TaskId — captura pra conferir depois contra o
        // GetTask real de apps/api.
        var taskId = await PollUntilAsync(
            () => GetPendingDispatchTaskIdAsync(channelId, externalId),
            id => id is not null,
            TimeSpan.FromSeconds(10));

        Assert.NotNull(taskId);

        // Round-trip completo: apps/workers processou a task (IChatClient
        // mockado) e a push notification real voltou para o endpoint
        // próprio de apps/inbox, que validou o token e removeu a
        // PendingDispatch (design.md, Decisão 6).
        await PollUntilAsync(
            () => HasPendingDispatchAsync(channelId, externalId),
            hasPending => !hasPending,
            TimeSpan.FromSeconds(20));

        // Confirma o conteúdo do outro lado: apps/api reflete a mesma task
        // como Completed, com o texto exato que o IChatClient mockado
        // devolveu — prova que o payload que chegou na push notification
        // era o correto, não só que "alguma" notificação chegou.
        var (state, artifactText) = await GetTaskFromApiAsync(agentId, taskId!);
        Assert.Equal("TASK_STATE_COMPLETED", state);
        Assert.Contains(RoundTripFixture.MockedAgentReplyText, artifactText);
    }

    [Fact]
    public async Task OperatorToken_IssuedByApi_IsAcceptedByInboxWithoutNetworkCallToApi()
    {
        // Prova o Risco 1 do design.md: apps/inbox valida o token
        // localmente, só pela chave de assinatura compartilhada — não
        // chama apps/api pra validar. A prova real disso é estrutural,
        // não deste teste isolado: Api:BaseUrl da instância de apps/inbox
        // deste fixture é "http://apps-api.test" (RoundTripFixture,
        // BuildInboxFactory), um host que não existe e não tem nenhum
        // handler de teste redirecionando-o para o TestServer de apps/api
        // (diferente do client nomeado do A2AClientFactory, que tem). Se
        // a validação do token dependesse de uma chamada de rede a
        // apps/api, esta requisição teria que falhar ou dar timeout — ela
        // não falha.
        var token = await fixture.LoginAsOperatorAsync();

        var inboxClient = fixture.InboxFactory.CreateClient();
        inboxClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await inboxClient.GetAsync("/channels");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<Guid> CreateAgentAsync()
    {
        var token = await fixture.LoginAsOperatorAsync();
        var client = fixture.ApiFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/agents",
            new { name = "Agente Round-Trip", instructions = "Responda com simpatia.", provider = "openai", model = "gpt-5.6-sol" });
        response.EnsureSuccessStatusCode();

        var agent = await response.Content.ReadFromJsonAsync<JsonElement>();
        return agent.GetProperty("id").GetGuid();
    }

    private async Task<(Guid ChannelId, string ExternalId)> CreateChannelAsync(Guid agentId)
    {
        // Criado direto no banco de apps/inbox, não via POST /channels: a
        // validação do AgentId contra apps/api (AgentReferenceValidator) já
        // tem cobertura própria em CreateChannelCommandHandlerTests — este
        // teste foca no round-trip debounce → SendMessage → push
        // notification, não na validação de cadastro de canal.
        using var scope = fixture.InboxFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InboxAppDbContext>();

        // "test-channel" — mesmo identificador de adapter de teste usado em
        // todo apps/inbox desde inbox-adapter-contrato-catalogo, quando
        // ChannelType deixou de ser um enum fechado (removido) para virar
        // string validada contra adapters registrados.
        var channel = new InboxChannel("test-channel", $"Canal Round-Trip {Guid.NewGuid()}", "irrelevante-nesta-fatia", agentId);
        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync();

        return (channel.Id, $"+5511{Guid.NewGuid():N}"[..15]);
    }

    private async Task<string?> GetPendingDispatchTaskIdAsync(Guid channelId, string externalId)
    {
        var dispatch = await FindPendingDispatchAsync(channelId, externalId);
        return dispatch?.TaskId;
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

    private async Task<(string State, string ArtifactText)> GetTaskFromApiAsync(Guid agentId, string taskId)
    {
        var token = await fixture.LoginAsOperatorAsync();
        var client = fixture.ApiFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var payload = new { jsonrpc = "2.0", id = 1, method = "GetTask", @params = new { id = taskId } };

        var response = await client.PostAsJsonAsync($"/agents/{agentId}/a2a", payload);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.TryGetProperty("error", out _));

        var result = body.GetProperty("result");
        var state = result.GetProperty("status").GetProperty("state").GetString()!;

        var artifactText = result.GetProperty("artifacts")
            .EnumerateArray()
            .SelectMany(artifact => artifact.GetProperty("parts").EnumerateArray())
            .Select(part => part.TryGetProperty("text", out var text) ? text.GetString() : null)
            .FirstOrDefault(text => text is not null) ?? "";

        return (state, artifactText);
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
