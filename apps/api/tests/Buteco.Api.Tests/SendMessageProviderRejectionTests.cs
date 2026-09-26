using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Agents.Requests;
using Buteco.Api.Agents.Responses;
using Buteco.Api.Infrastructure;
using Buteco.Api.RejectionMetrics;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests;

public class SendMessageProviderRejectionTests(AgentProviderRejectionFixture fixture) : IClassFixture<AgentProviderRejectionFixture>
{
    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task SendMessage_ForAgentWithoutProviderOrModel_RejectsTaskWithoutPublishing()
    {
        var agentId = await SeedLegacyAgentWithoutProviderOrModelAsync();

        var sendBody = await SendMessageAsync(agentId);

        Assert.False(sendBody.TryGetProperty("error", out _));
        var taskElement = sendBody.GetProperty("result").GetProperty("task");
        var taskId = taskElement.GetProperty("id").GetString()!;
        Assert.Equal("TASK_STATE_REJECTED", taskElement.GetProperty("status").GetProperty("state").GetString());

        Assert.Empty(fixture.TaskJobPublisher.PublishedMessages);

        var getPayload = new { jsonrpc = "2.0", id = 2, method = "GetTask", @params = new { id = taskId } };
        var getResponse = await _client.PostAsJsonAsync($"/agents/{agentId}/a2a", getPayload);
        var getBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(getBody.TryGetProperty("error", out _));
        Assert.Equal("TASK_STATE_REJECTED", getBody.GetProperty("result").GetProperty("status").GetProperty("state").GetString());
    }

    [Fact]
    public async Task SendMessage_ForAgentWithProviderThatBecameUnconfigured_RejectsTaskWithoutPublishing()
    {
        var configuration = fixture.Services.GetRequiredService<IConfiguration>();

        // Gemini configurado no momento do cadastro do agente.
        configuration["Gemini:ApiKey"] = "some-key";
        var agentId = await CreateAgentAsync("gemini", "gemini-3.6-flash");

        // Chave removida do ambiente entre o cadastro e o SendMessage.
        configuration["Gemini:ApiKey"] = null;

        var sendBody = await SendMessageAsync(agentId);

        Assert.False(sendBody.TryGetProperty("error", out _));
        var taskElement = sendBody.GetProperty("result").GetProperty("task");
        Assert.Equal("TASK_STATE_REJECTED", taskElement.GetProperty("status").GetProperty("state").GetString());

        Assert.Empty(fixture.TaskJobPublisher.PublishedMessages);
    }

    // ------------------------------------------- O MOTIVO DA RECUSA (M29, #51)

    [Fact]
    public async Task SendMessage_ForAgentWithoutProviderOrModel_RecordsProviderOrModelMissing()
    {
        var agentId = await SeedLegacyAgentWithoutProviderOrModelAsync();

        var taskId = await RejectedTaskIdAsync(agentId);

        // O VALOR, e não "é diferente dos outros": escrito como diferença, o guarda
        // ficaria verde com as quatro causas gravando o mesmo motivo — é a quinta
        // forma da convenção 15, critério que o acerto e o erro satisfazem juntos.
        Assert.Equal(
            RejectionMetricsValues.Reason.ProviderOrModelMissing,
            await RejectionReasonAsync(taskId));
    }

    [Fact]
    public async Task SendMessage_ForAgentWithProviderThatBecameUnconfigured_RecordsProviderNotConfigured()
    {
        var configuration = fixture.Services.GetRequiredService<IConfiguration>();

        configuration["Gemini:ApiKey"] = "some-key";
        var agentId = await CreateAgentAsync("gemini", "gemini-3.6-flash");
        configuration["Gemini:ApiKey"] = null;

        var taskId = await RejectedTaskIdAsync(agentId);

        Assert.Equal(
            RejectionMetricsValues.Reason.ProviderNotConfigured,
            await RejectionReasonAsync(taskId));
    }

    /// <summary>
    /// A quarta causa — <c>AgentNotFound</c> —, e o caso carrega DOIS fatos que a
    /// escrita do guarda separou (design.md, D4).
    ///
    /// <para>
    /// <b>O motivo é gravado certo:</b> o handler chega ao caminho de agente não
    /// encontrado e a linha de métrica nasce com <c>AgentNotFound</c>, nunca com
    /// <c>AgentInactive</c>. Ela só pode nascer porque <c>task_rejections</c>
    /// <b>não tem FK</b> — é a D2 pagando no primeiro caso que a exercita.
    /// </para>
    ///
    /// <para>
    /// <b>E a resposta do protocolo NÃO é <c>rejected</c>:</b> <c>a2a_tasks</c> tem
    /// FK para <c>agents</c>, então com a linha do agente apagada a task não chega a
    /// ser persistida e o cliente recebe erro interno. É estado que o protocolo
    /// tem como responder e não responde — <b>achado com issue própria</b>, fora do
    /// escopo desta change, que é coleta.
    /// </para>
    ///
    /// <para>
    /// O arranjo explora o cache do <c>AgentA2AServerRegistry</c>: ele confere
    /// existência UMA vez por agente e guarda o servidor, então a segunda chamada
    /// chega ao handler sem a linha do agente. É o único caminho alcançável hoje,
    /// porque <c>Agent</c> não tem rota de exclusão.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SendMessage_ForAgentDeletedAfterTheServerWasResolved_RecordsAgentNotFound_ThoughTheProtocolAnswersError()
    {
        var agentId = await SeedLegacyAgentWithoutProviderOrModelAsync();

        // Primeira chamada: resolve e cacheia o A2AServer deste agente.
        var firstTaskId = await RejectedTaskIdAsync(agentId);

        await DeleteAgentRowAsync(agentId);

        var body = await SendMessageAsync(agentId);

        // O que o protocolo devolve HOJE neste estado: erro interno, e não a task
        // em `rejected`. A task não chega a existir, então não há id pelo qual
        // procurar a recusa — é por isso que a asserção do motivo é por agente.
        Assert.True(body.TryGetProperty("error", out var error), body.ToString());
        Assert.Equal(-32603, error.GetProperty("code").GetInt32());

        using var scope = fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var reasons = await dbContext.TaskRejections
            .AsNoTracking()
            .Where(rejection => rejection.AgentId == agentId && rejection.TaskId != firstTaskId)
            .Select(rejection => rejection.Reason)
            .ToListAsync();

        // O VALOR, e a negativa ao lado: colapsar "não existe" em "está inativo"
        // afirmaria um estado que ninguém leu (convenção 13).
        Assert.Equal([RejectionMetricsValues.Reason.AgentNotFound], reasons);
        Assert.DoesNotContain(RejectionMetricsValues.Reason.AgentInactive, reasons);
    }

    /// <summary>
    /// A ausência de chave estrangeira de <c>task_rejections</c>, que a D2 decidiu,
    /// tem contraparte verificável (convenção 10): <c>a2a_tasks</c> tem FK para
    /// <c>agents</c> com cascade, então a exclusão do agente apaga as tasks dele —
    /// e a linha de recusa <b>fica</b>. A métrica registra o que aconteceu, e isso
    /// não muda porque o catálogo mudou depois.
    /// </summary>
    [Fact]
    public async Task RejectionRow_SurvivesAgentDeletion_BecauseItHasNoForeignKey()
    {
        var agentId = await SeedLegacyAgentWithoutProviderOrModelAsync();
        var taskId = await RejectedTaskIdAsync(agentId);

        await DeleteAgentRowAsync(agentId);

        using var scope = fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.False(
            await dbContext.A2ATasks.AsNoTracking().AnyAsync(task => task.TaskId == taskId),
            "a task do protocolo é apagada pela cascade de agents — se isto falhar, o arranjo deixou de exercer o ponto");

        // A ASSERÇÃO É A EXISTÊNCIA DA LINHA, e não o valor do motivo — corrigido
        // pela mutação da tarefa 2.7: com o valor, quebrar a atribuição de uma
        // causa reprovava este guarda junto, por um motivo que não tem relação com
        // o que ele protege. Guarda que reprova pelo defeito de outro não diz onde
        // está o defeito (convenção 15, segunda forma).
        Assert.True(
            await dbContext.TaskRejections.AsNoTracking().AnyAsync(rejection => rejection.TaskId == taskId),
            "a linha de recusa tem de sobreviver à exclusão do agente — é a ausência de FK que esta asserção prende");
    }

    private async Task<string> RejectedTaskIdAsync(Guid agentId)
    {
        var body = await SendMessageAsync(agentId);
        var task = body.GetProperty("result").GetProperty("task");

        Assert.Equal("TASK_STATE_REJECTED", task.GetProperty("status").GetProperty("state").GetString());

        return task.GetProperty("id").GetString()!;
    }

    private async Task<string?> RejectionReasonAsync(string taskId)
    {
        using var scope = fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await dbContext.TaskRejections
            .AsNoTracking()
            .Where(rejection => rejection.TaskId == taskId)
            .Select(rejection => rejection.Reason)
            .SingleOrDefaultAsync();
    }

    private async Task DeleteAgentRowAsync(Guid agentId)
    {
        using var scope = fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM agents WHERE \"Id\" = {agentId}");
    }

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

    private async Task<Guid> CreateAgentAsync(string provider, string model)
    {
        var response = await _client.PostAsJsonAsync("/agents", new CreateAgentRequest("Atendente", "Instruções.", provider, model));
        response.EnsureSuccessStatusCode();
        var agent = await response.Content.ReadFromJsonAsync<AgentResponse>();
        return agent!.Id;
    }

    private async Task<Guid> SeedLegacyAgentWithoutProviderOrModelAsync()
    {
        using var scope = fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var agentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "IsActive", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {"Legado"}, {"Instruções legadas."}, {true}, {now}, {now})
             """);

        return agentId;
    }
}
