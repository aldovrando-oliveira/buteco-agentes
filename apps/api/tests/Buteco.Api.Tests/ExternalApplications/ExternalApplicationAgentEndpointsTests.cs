using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.ExternalApplications;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Tests.ExternalApplications;

/// <summary>
/// Os agentes liberados para cada aplicação e as aplicações com acesso a cada agente (change
/// agentes-por-aplicacao, D5; spec <c>external-application-agents</c>; seções 1 e 6 do tasks.md).
/// </summary>
public class ExternalApplicationAgentEndpointsTests(ExternalApplicationsFixture factory) : IClassFixture<ExternalApplicationsFixture>
{
    // (guarda 1.4) A lista vazia é recusada também pelo banco, e os valores repetidos são gravados uma vez.
    // Defeito: o CHECK removido da configuração e da migration.
    [Fact]
    public async Task TheDatabase_RefusesAnEmptyList_AndRepeatedValuesAreStoredOnce()
    {
        var agent = await AgentAsync();
        var application = await factory.CreateAsync();

        var empty = await Record.ExceptionAsync(() => factory.WithDbAsync(async db =>
        {
            db.ExternalApplicationAgents.Add(ExternalApplicationAgent.Create(application.Id, agent, [], DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }));
        Assert.IsType<DbUpdateException>(empty);

        var granted = await GrantAsync(application.Id, agent, "consultar-task", "enviar-mensagem", "consultar-task");
        Assert.Equal(HttpStatusCode.Created, granted.StatusCode);
        Assert.Equal(["enviar-mensagem", "consultar-task"], await StoredAsync(application.Id, agent));
    }

    // "Aplicação nasce sem agentes", "Liberar com duas operações" (na ordem da tabela) e "Desativar e reativar
    // não apaga o vínculo".
    [Fact]
    public async Task ANewApplication_HasNoAgents_AndALinkSurvivesDeactivation()
    {
        var agent = await AgentAsync("Atendimento Varejo");
        var application = await factory.CreateAsync();
        var admin = await factory.AdminClientAsync();
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>($"/applications/{application.Id}/agents")).EnumerateArray());

        var granted = await GrantAsync(application.Id, agent, "consultar-task", "enviar-mensagem");
        var item = await granted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Created, granted.StatusCode);
        Assert.Equal(agent, item.GetProperty("agenteId").GetGuid());
        Assert.Equal("Atendimento Varejo", item.GetProperty("nome").GetString());
        Assert.True(item.GetProperty("ativo").GetBoolean());
        Assert.Equal(["enviar-mensagem", "consultar-task"], Operations(item));

        (await admin.PostAsync($"/applications/{application.Id}/deactivate", content: null)).EnsureSuccessStatusCode();
        (await admin.PostAsync($"/applications/{application.Id}/activate", content: null)).EnsureSuccessStatusCode();
        var list = (await admin.GetFromJsonAsync<JsonElement>($"/applications/{application.Id}/agents")).EnumerateArray().ToList();
        Assert.Equal(agent, Assert.Single(list).GetProperty("agenteId").GetGuid());
        Assert.Equal(["enviar-mensagem", "consultar-task"], Operations(list[0]));
    }

    // "Sem operação", "Cancelar não é operação" e "Agente inexistente": 400 no campo, sem vínculo.
    [Fact]
    public async Task InvalidGrants_AreRefusedByField_WithNoLink()
    {
        var agent = await AgentAsync();
        var application = await factory.CreateAsync();

        var empty = await GrantAsync(application.Id, agent);
        var cancel = await GrantAsync(application.Id, agent, "enviar-mensagem", "cancelar-task");
        var missing = await GrantAsync(application.Id, Guid.NewGuid(), "enviar-mensagem");

        Assert.Equal(["Marque ao menos uma operação."], await FieldAsync(empty, "operacoes"));
        Assert.Equal(["Operação desconhecida: cancelar-task."], await FieldAsync(cancel, "operacoes"));
        Assert.Equal(["Agente não encontrado."], await FieldAsync(missing, "agenteId"));
        Assert.False(await factory.WithDbAsync(db => db.ExternalApplicationAgents.AnyAsync(link => link.ApplicationId == application.Id)));
    }

    // "Aplicação inexistente", na lista e na liberação.
    [Fact]
    public async Task AnUnknownApplication_IsNotFound()
    {
        var admin = await factory.AdminClientAsync();
        var list = await admin.GetAsync($"/applications/{Guid.NewGuid()}/agents");
        var grant = await GrantAsync(Guid.NewGuid(), await AgentAsync(), "enviar-mensagem");

        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal("Aplicação não encontrada.", (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, grant.StatusCode);
    }

    // (guarda 6.3) "Agente já liberado": 409, e as operações gravadas não mudam. Defeito: o POST gravando por
    // cima (upsert).
    [Fact]
    public async Task GrantingAgain_IsAConflict_AndKeepsTheOperations()
    {
        var agent = await AgentAsync();
        var application = await factory.CreateAsync();
        (await GrantAsync(application.Id, agent, "consultar-task")).EnsureSuccessStatusCode();

        var again = await GrantAsync(application.Id, agent, "enviar-mensagem", "notificacao-push");

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("Este agente já está liberado para a aplicação.", (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Equal(["consultar-task"], await StoredAsync(application.Id, agent));
    }

    // "Agente inativo pode ser liberado" e "Agente inativo aparece com ativo false".
    [Fact]
    public async Task AnInactiveAgent_CanBeGranted_AndIsListedAsInactive()
    {
        var agent = await AgentAsync();
        var admin = await factory.AdminClientAsync();
        (await admin.PostAsync($"/agents/{agent}/deactivate", content: null)).EnsureSuccessStatusCode();
        var application = await factory.CreateAsync();

        var granted = await GrantAsync(application.Id, agent, "enviar-mensagem");

        Assert.Equal(HttpStatusCode.Created, granted.StatusCode);
        Assert.False((await granted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ativo").GetBoolean());
        var listed = (await admin.GetFromJsonAsync<JsonElement>($"/applications/{application.Id}/agents")).EnumerateArray().Single();
        Assert.False(listed.GetProperty("ativo").GetBoolean());
    }

    // "Trocar as operações" (e o SendMessage seguinte recebe 403), "Retirar" (204, fora da lista, e a A2A
    // seguinte recebe 404) e "Retirado entre abrir e salvar" (404).
    [Fact]
    public async Task ChangingAndRemoving_TakeEffectOnTheNextA2ACall()
    {
        var agent = await AgentAsync();
        var application = await factory.CreateAsync();
        var admin = await factory.AdminClientAsync();
        (await GrantAsync(application.Id, agent, "enviar-mensagem", "consultar-task")).EnsureSuccessStatusCode();
        var client = factory.WithToken(await factory.AccessTokenAsync(application.ClientId, application.Secret));

        var changed = await admin.PutAsJsonAsync($"/applications/{application.Id}/agents/{agent}", new { operacoes = new[] { "consultar-task" } });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(["consultar-task"], Operations(await changed.Content.ReadFromJsonAsync<JsonElement>()));
        Assert.Equal(HttpStatusCode.Forbidden, (await SendMessageAsync(client, agent)).StatusCode);

        var removed = await admin.DeleteAsync($"/applications/{application.Id}/agents/{agent}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>($"/applications/{application.Id}/agents")).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await SendMessageAsync(client, agent)).StatusCode);

        var late = await admin.PutAsJsonAsync($"/applications/{application.Id}/agents/{agent}", new { operacoes = new[] { "enviar-mensagem" } });
        Assert.Equal(HttpStatusCode.NotFound, late.StatusCode);
        Assert.Equal("Agente não liberado para esta aplicação.", (await late.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/applications/{application.Id}/agents/{agent}")).StatusCode);
    }

    // (6.3b, Q11) "Os agentes liberados na lista": pelo nome, vazia quando não há nenhum.
    [Fact]
    public async Task TheList_CarriesTheGrantedAgents_ByName()
    {
        var triage = await AgentAsync("Triagem de Pedidos");
        var retail = await AgentAsync("Atendimento Varejo");
        var portal = await factory.CreateAsync();
        var chatbot = await factory.CreateAsync();
        (await GrantAsync(portal.Id, triage, "enviar-mensagem")).EnsureSuccessStatusCode();
        (await GrantAsync(portal.Id, retail, "consultar-task")).EnsureSuccessStatusCode();

        var list = (await (await factory.AdminClientAsync()).GetFromJsonAsync<JsonElement>("/applications/")).EnumerateArray().ToList();

        var portalAgents = list.Single(item => item.GetProperty("id").GetGuid() == portal.Id).GetProperty("agentes").EnumerateArray().ToList();
        Assert.Equal(["Atendimento Varejo", "Triagem de Pedidos"], portalAgents.Select(agent => agent.GetProperty("nome").GetString()!));
        Assert.Equal([retail, triage], portalAgents.Select(agent => agent.GetProperty("id").GetGuid()));
        Assert.Empty(list.Single(item => item.GetProperty("id").GetGuid() == chatbot.Id).GetProperty("agentes").EnumerateArray());
    }

    // "Leitor vê só a contagem", "Admin vê a lista" e "Agente sem aplicação"; e nenhuma das duas traz client
    // ID nem segredo, e a contagem não traz nome nem id.
    [Fact]
    public async Task TheAgentCard_CountsForEveryRole_AndListsOnlyForTheAdmin()
    {
        var agent = await AgentAsync();
        var lonely = await AgentAsync();
        var applications = new List<ExternalApplicationsFixture.Created>();
        foreach (var name in new[] { "Zeta parceira", "Alfa parceira", "Beta legada" })
        {
            var created = await factory.CreateAsync(name: $"{name} {Guid.NewGuid():N}"[..24]);
            (await GrantAsync(created.Id, agent, "enviar-mensagem")).EnsureSuccessStatusCode();
            applications.Add(created);
        }

        var admin = await factory.AdminClientAsync();
        (await admin.PostAsync($"/applications/{applications[2].Id}/deactivate", content: null)).EnsureSuccessStatusCode();
        var reader = factory.As(await SignedInUser.CreateAsync(factory, "card-leitor", role: UserRole.Reader));
        var operatorUser = factory.As(await SignedInUser.CreateAsync(factory, "card-operador", role: UserRole.Operator));

        var count = await reader.GetAsync($"/agents/{agent}/applications/count");
        var countText = await count.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, count.StatusCode);
        Assert.Equal("""{"total":3,"ativas":2}""", countText);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync($"/agents/{agent}/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorUser.GetAsync($"/agents/{agent}/applications")).StatusCode);
        Assert.Equal("""{"total":0,"ativas":0}""", await (await operatorUser.GetAsync($"/agents/{lonely}/applications/count")).Content.ReadAsStringAsync());

        var listResponse = await admin.GetAsync($"/agents/{agent}/applications");
        var listText = await listResponse.Content.ReadAsStringAsync();
        var list = JsonDocument.Parse(listText).RootElement.EnumerateArray().ToList();
        Assert.Equal(3, list.Count);
        Assert.Equal([true, true, false], list.Select(item => item.GetProperty("ativa").GetBoolean()));
        Assert.Equal(applications[2].Id, list[2].GetProperty("id").GetGuid());
        Assert.True(string.CompareOrdinal(list[0].GetProperty("nome").GetString()!.ToUpperInvariant(), list[1].GetProperty("nome").GetString()!.ToUpperInvariant()) < 0);
        Assert.Equal(["enviar-mensagem"], Operations(list[0]));
        Assert.True(list[0].TryGetProperty("ultimoUso", out _));
        foreach (var application in applications)
        {
            Assert.DoesNotContain(application.ClientId, listText, StringComparison.Ordinal);
            Assert.DoesNotContain(application.Secret, listText, StringComparison.Ordinal);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/agents/{Guid.NewGuid()}/applications/count")).StatusCode);
    }

    // "Só o Admin libera agentes para uma aplicação" (route-roles): o Operador recebe 403, sem vínculo.
    [Fact]
    public async Task AnOperator_CannotGrant()
    {
        var agent = await AgentAsync();
        var application = await factory.CreateAsync();
        var operatorUser = factory.As(await SignedInUser.CreateAsync(factory, "vinculo-operador", role: UserRole.Operator));

        var response = await operatorUser.PostAsJsonAsync($"/applications/{application.Id}/agents", new { agenteId = agent, operacoes = new[] { "enviar-mensagem" } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await factory.WithDbAsync(db => db.ExternalApplicationAgents.AnyAsync(link => link.ApplicationId == application.Id)));
    }

    // (guarda 6.6) "Admin rebaixado perde as rotas do vínculo na requisição seguinte" (route-roles, "Prazos"):
    // o token ainda traz admin, e o papel gravado decide. Defeito: a chamada do CurrentUser removida do POST.
    [Fact]
    public async Task ADemotedAdmin_LosesTheLinkRoutes_OnTheNextRequest()
    {
        var agent = await AgentAsync();
        var application = await factory.CreateAsync();
        var demoted = await SignedInUser.CreateAsync(factory, "vinculo-rebaixado");
        await LocalUserSeed.SetRoleAsync(factory.Services, demoted.User.Id, UserRole.Operator);
        var client = factory.As(demoted);

        var grant = await client.PostAsJsonAsync($"/applications/{application.Id}/agents", new { agenteId = agent, operacoes = new[] { "enviar-mensagem" } });

        Assert.Equal(HttpStatusCode.Forbidden, grant.StatusCode);
        Assert.False(await factory.WithDbAsync(db => db.ExternalApplicationAgents.AnyAsync(link => link.ApplicationId == application.Id)));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/agents/{agent}/applications")).StatusCode);
    }

    // ---- passos ----

    private async Task<Guid> AgentAsync(string? name = null)
    {
        var response = await (await factory.AdminClientAsync()).PostAsJsonAsync(
            "/agents/",
            new { name = name ?? $"Agente {Guid.NewGuid():N}"[..20], instructions = "Responda.", provider = "openai", model = "gpt-5.6-sol" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> GrantAsync(Guid applicationId, Guid agentId, params string[] operations) =>
        await (await factory.AdminClientAsync()).PostAsJsonAsync($"/applications/{applicationId}/agents", new { agenteId = agentId, operacoes = operations });

    private static Task<HttpResponseMessage> SendMessageAsync(HttpClient client, Guid agentId) =>
        client.PostAsJsonAsync($"/agents/{agentId}/a2a", new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "SendMessage",
            @params = new { message = new { role = "ROLE_USER", parts = new[] { new { text = "Olá" } }, messageId = Guid.NewGuid().ToString("N") } },
        });

    private static string[] Operations(JsonElement item) =>
        item.GetProperty("operacoes").EnumerateArray().Select(value => value.GetString()!).ToArray();

    private static async Task<string[]> FieldAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("errors").GetProperty(field).EnumerateArray().Select(value => value.GetString()!).ToArray();
    }

    private Task<List<string>> StoredAsync(Guid applicationId, Guid agentId) => factory.WithDbAsync(db =>
        db.ExternalApplicationAgents.AsNoTracking()
            .Where(link => link.ApplicationId == applicationId && link.AgentId == agentId)
            .Select(link => link.Operations)
            .SingleAsync());
}
