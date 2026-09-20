using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Messaging;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

// AS ASSERÇÕES SOBRE O PUBLISHER SÃO POR AGENTE, E NÃO POR "NADA FOI
// PUBLICADO" — ver design.md da change delegacao-ciclo-no-cadastro, D9.
// `AgentDeactivationFixture.TaskJobPublisher` é `IClassFixture`: uma instância
// só para a classe inteira, e `FakeTaskJobPublisher.PublishedMessages` acumula
// sem nunca esvaziar. Três testes afirmavam `Assert.Empty` sobre essa coleção
// enquanto `SendMessage_AfterReactivation_PublishesNormally` publica de
// verdade — então quem rodasse DEPOIS dele reprovava, e rodando isolado
// passava. Medido contra o HEAD 1ba84ec: a suíte inteira dava 317/318, a
// classe sozinha 2 de 3, e o teste sozinho passava. Era guarda que aprovava ou
// reprovava por sorte de ordenação, que é o defeito que a convenção 15 nomeia.
//
// NÃO TROCAR POR UM `Clear()` NO INÍCIO DE CADA TESTE. Ele também faria a
// classe passar hoje, e foi recusado: depende de os testes da classe não
// rodarem em paralelo, que é propriedade do runner e não do teste — mesma
// família do guarda cujo critério é uma ordem que o banco às vezes já produz
// sozinho (convenção 15, quinta forma). Cada teste aqui cria o próprio agente,
// então filtrar por `AgentId` é independente de ordem POR CONSTRUÇÃO: o
// resultado não muda se a classe rodar em qualquer ordem, em paralelo, ou com
// testes novos no meio.
public class AgentDeactivationTests(AgentDeactivationFixture fixture) : IClassFixture<AgentDeactivationFixture>
{
    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task SendMessage_ForInactiveAgent_RejectsTaskWithoutPublishing()
    {
        var agentId = await CreateAgentAsync();
        await DeactivateAgentAsync(agentId);

        var sendBody = await SendMessageAsync(agentId);

        Assert.False(sendBody.TryGetProperty("error", out _));
        var taskElement = sendBody.GetProperty("result").GetProperty("task");
        var taskId = taskElement.GetProperty("id").GetString()!;
        Assert.Equal("TASK_STATE_REJECTED", taskElement.GetProperty("status").GetProperty("state").GetString());

        AssertNothingPublishedFor(agentId);

        var getPayload = new { jsonrpc = "2.0", id = 2, method = "GetTask", @params = new { id = taskId } };
        var getResponse = await _client.PostAsJsonAsync($"/agents/{agentId}/a2a", getPayload);
        var getBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(getBody.TryGetProperty("error", out _));
        Assert.Equal("TASK_STATE_REJECTED", getBody.GetProperty("result").GetProperty("status").GetProperty("state").GetString());
    }

    [Fact]
    public async Task SendMessage_WithPushNotificationConfig_ForInactiveAgent_NeverPublishesJobOrCallsWebhook()
    {
        // Cobre specs/a2a-push-notifications/spec.md, Scenario "Task rejeitada
        // nunca dispara o webhook" — rejeição síncrona em EnqueueingAgentHandler
        // acontece antes de qualquer TaskJobMessage existir, então mesmo com
        // pushNotificationConfig registrado, nada chega a apps/workers (nada
        // que poderia disparar o webhook é sequer publicado).
        var agentId = await CreateAgentAsync();
        await DeactivateAgentAsync(agentId);

        var messageId = Guid.NewGuid().ToString("N");
        var payload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "SendMessage",
            @params = new
            {
                message = new
                {
                    role = "ROLE_USER",
                    parts = new[] { new { text = "Olá, tudo bem?" } },
                    messageId,
                },
                configuration = new
                {
                    pushNotificationConfig = new { url = "https://cliente.example.com/webhooks/a2a" },
                },
            },
        };

        var response = await _client.PostAsJsonAsync($"/agents/{agentId}/a2a", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "TASK_STATE_REJECTED",
            body.GetProperty("result").GetProperty("task").GetProperty("status").GetProperty("state").GetString());

        AssertNothingPublishedFor(agentId);
    }

    [Fact]
    public async Task SendMessage_AfterReactivation_PublishesNormally()
    {
        var agentId = await CreateAgentAsync();
        await DeactivateAgentAsync(agentId);

        var rejectedBody = await SendMessageAsync(agentId);
        Assert.Equal(
            "TASK_STATE_REJECTED",
            rejectedBody.GetProperty("result").GetProperty("task").GetProperty("status").GetProperty("state").GetString());
        AssertNothingPublishedFor(agentId);

        await ActivateAgentAsync(agentId);

        var submittedBody = await SendMessageAsync(agentId);
        var taskElement = submittedBody.GetProperty("result").GetProperty("task");
        var taskId = taskElement.GetProperty("id").GetString()!;
        Assert.Equal("TASK_STATE_SUBMITTED", taskElement.GetProperty("status").GetProperty("state").GetString());

        var published = Assert.Single(PublishedFor(agentId));
        Assert.Equal(taskId, published.TaskId);
        Assert.Equal(agentId, published.AgentId);
    }

    private IReadOnlyList<TaskJobMessage> PublishedFor(Guid agentId) =>
        fixture.TaskJobPublisher.PublishedMessages.Where(message => message.AgentId == agentId).ToList();

    private void AssertNothingPublishedFor(Guid agentId) => Assert.Empty(PublishedFor(agentId));

    private async Task<JsonElement> SendMessageAsync(Guid agentId)
    {
        var messageId = Guid.NewGuid().ToString("N");
        var payload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "SendMessage",
            @params = new
            {
                message = new
                {
                    role = "ROLE_USER",
                    parts = new[] { new { text = "Olá, tudo bem?" } },
                    messageId,
                },
            },
        };

        var response = await _client.PostAsJsonAsync($"/agents/{agentId}/a2a", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Guid> CreateAgentAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/agents",
            new { name = "Atendente", instructions = "Responda com simpatia.", provider = "openai", model = "gpt-5.6-sol" });
        response.EnsureSuccessStatusCode();
        var agent = await response.Content.ReadFromJsonAsync<JsonElement>();
        return agent.GetProperty("id").GetGuid();
    }

    private async Task DeactivateAgentAsync(Guid agentId)
    {
        var response = await _client.PostAsync($"/agents/{agentId}/deactivate", content: null);
        response.EnsureSuccessStatusCode();
    }

    private async Task ActivateAgentAsync(Guid agentId)
    {
        var response = await _client.PostAsync($"/agents/{agentId}/activate", content: null);
        response.EnsureSuccessStatusCode();
    }
}
