using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Buteco.Api.ExternalApplications;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Tests.ExternalApplications;

/// <summary>
/// A rota A2A para a aplicação externa (change agentes-por-aplicacao, D1 a D3; specs
/// <c>a2a-task-lifecycle</c> e <c>a2a-push-notifications</c>; seções 3 e 4 do tasks.md). Tudo pelo caminho
/// real: o Admin cria a aplicação e libera o agente pelas rotas, a aplicação pede o token no
/// <c>/connect/token</c>, e a chamada passa pelo filtro, pelo SDK e pelo store de verdade. A fila é a falsa
/// da fixture, para afirmar o que foi (e o que não foi) publicado.
/// </summary>
public class ApplicationA2ARouteTests(ExternalApplicationsFixture factory) : IClassFixture<ExternalApplicationsFixture>
{
    private const string SendMessage = ExternalApplicationOperations.SendMessage;
    private const string GetTask = ExternalApplicationOperations.GetTask;
    private const string Push = ExternalApplicationOperations.PushNotification;

    // (guarda 3.2) "O 404 não revela se o agente existe". Três chamadas da mesma forma, para um agente que
    // não existe, um que existe sem vínculo e, com a aplicação já desativada e o token ainda no prazo, um
    // com vínculo: status, cabeçalhos de conteúdo e corpo iguais. Defeitos: (a) o filtro lendo agents antes
    // do vínculo e devolvendo o -32600 do SDK para o inexistente; (b) o filtro sem a condição de ativa.
    [Fact]
    public async Task TheNotFound_IsTheSame_ForAMissingAgent_AnUnlinkedAgent_AndADeactivatedApplication()
    {
        var linked = await AgentAsync();
        var unlinked = await AgentAsync();
        var app = await ApplicationAsync((linked, [SendMessage]));
        var deactivated = await ApplicationAsync((linked, [SendMessage]));
        var deactivatedClient = deactivated.Client;
        await DeactivateAsync(deactivated.Id);

        var missing = await RawAsync(app.Client, Guid.NewGuid(), Send());
        var notLinked = await RawAsync(app.Client, unlinked, Send());
        var off = await RawAsync(deactivatedClient, linked, Send());

        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
        Assert.Equal(missing, notLinked);
        Assert.Equal(missing, off);
        Assert.Empty(missing.Body);
        Assert.Equal(0, await TaskCountAsync(unlinked));
        Assert.Equal(0, await TaskCountAsync(linked));
    }

    // "Aplicação desativada perde a A2A na hora" e "Desativar corta a A2A na hora" (external-applications):
    // o GetTask de uma task dela, com o token de antes, e o agente continua liberado.
    [Fact]
    public async Task ADeactivatedApplication_LosesTheA2A_AtOnce_AndKeepsItsAgents()
    {
        var agent = await AgentAsync();
        var app = await ApplicationAsync((agent, [SendMessage, GetTask]));
        var taskId = TaskIdOf(await RpcAsync(app.Client, agent, Send()));

        await DeactivateAsync(app.Id);
        var response = await RawAsync(app.Client, agent, Get(taskId));

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        var agents = await (await factory.AdminClientAsync()).GetFromJsonAsync<JsonElement>($"/applications/{app.Id}/agents");
        Assert.Equal(agent, agents.EnumerateArray().Single().GetProperty("agenteId").GetGuid());
    }

    // (guarda 3.4) A matriz método × operação, com a expectativa escrita aqui, à parte do mapa de produção.
    // Para cada conjunto de operações, cada um dos 11 métodos do SDK e um nome desconhecido. Defeitos: (a)
    // ListTasks mapeado a consultar-task; (b) CancelTask mapeado a enviar-mensagem.
    [Fact]
    public async Task EachMethod_ForEachSetOfOperations_FollowsTheMatrix()
    {
        var agent = await AgentAsync();
        string[][] sets = [[SendMessage, GetTask, Push], [GetTask, Push], [SendMessage, Push], [SendMessage, GetTask]];
        string[] others =
        [
            "ListTasks", "CancelTask", "SendStreamingMessage", "SubscribeToTask", "CreateTaskPushNotificationConfig",
            "GetTaskPushNotificationConfig", "ListTaskPushNotificationConfig", "DeleteTaskPushNotificationConfig",
            "GetExtendedAgentCard", "TarefaInventada",
        ];
        var wrong = new List<string>();
        foreach (var set in sets)
        {
            var app = await ApplicationAsync((agent, set));
            var expectedSend = set.Contains(SendMessage) ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
            var expectedGet = set.Contains(GetTask) ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
            Check(set, "SendMessage", expectedSend, (await RawAsync(app.Client, agent, Send())).Status);
            Check(set, "GetTask", expectedGet, (await RawAsync(app.Client, agent, Get(Guid.NewGuid().ToString("N")))).Status);
            foreach (var method in others)
            {
                Check(set, method, HttpStatusCode.Forbidden, (await RawAsync(app.Client, agent, Call(method, $$"""{"id":"{{Guid.NewGuid():N}}"}"""))).Status);
            }
        }

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));

        void Check(string[] set, string method, HttpStatusCode expected, HttpStatusCode actual)
        {
            if (expected != actual)
            {
                wrong.Add($"[{string.Join(",", set)}] {method}: esperado {(int)expected}, veio {(int)actual}");
            }
        }
    }

    // "Cancelar está fora das operações" (Q1, #264): a task própria em submitted continua submitted.
    [Fact]
    public async Task CancelTask_IsForbidden_AndTheTaskStaysSubmitted()
    {
        var agent = await AgentAsync();
        var app = await ApplicationAsync((agent, [SendMessage, GetTask, Push]));
        var taskId = TaskIdOf(await RpcAsync(app.Client, agent, Send()));

        var response = await RawAsync(app.Client, agent, Call("CancelTask", $$"""{"id":"{{taskId}}"}"""));

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal("Submitted", await StateAsync(taskId));
    }

    // "Listar tasks está fora das operações": sem nenhuma task no corpo.
    [Fact]
    public async Task ListTasks_IsForbidden_WithNoTaskInTheBody()
    {
        var agent = await AgentAsync();
        var app = await ApplicationAsync((agent, [SendMessage, GetTask, Push]));
        await RpcAsync(app.Client, agent, Send());

        var response = await RawAsync(app.Client, agent, Call("ListTasks", "{}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Empty(response.Body);
    }

    // (guarda 3.5) "Push sem a operação" e "Push com a operação" (a2a-push-notifications). Defeito: o filtro
    // olhando só o method, sem a configuração.
    [Fact]
    public async Task APushConfig_NeedsThePushOperation_AndWithItTheJobCarriesIt()
    {
        var agent = await AgentAsync();
        var withoutPush = await ApplicationAsync((agent, [SendMessage]));
        var withPush = await ApplicationAsync((agent, [SendMessage, Push]));
        var before = await TaskCountAsync(agent);

        var refused = await RawAsync(withoutPush.Client, agent, Send(push: true));
        var accepted = TaskIdOf(await RpcAsync(withPush.Client, agent, Send(push: true)));

        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
        Assert.Equal(before + 1, await TaskCountAsync(agent));
        var job = Assert.Single(factory.TaskJobPublisher.PublishedMessages, message => message.AgentId == agent);
        Assert.Equal(accepted, job.TaskId);
        Assert.Equal("https://cliente.example.com/webhooks/a2a", job.PushNotificationConfig?.Url);
    }

    // (guarda 3.5b, pedido do dono) "Push sem a operação": a aplicação com enviar-mensagem e sem
    // notificacao-push manda SendMessage com push e recebe 403, sem task e sem job. Defeito: o filtro
    // aceitando pushNotificationConfig sem a operação de push.
    [Fact]
    public async Task APushConfig_WithoutThePushOperation_IsForbidden_WithNoTaskAndNoJob()
    {
        var agent = await AgentAsync();
        var app = await ApplicationAsync((agent, [SendMessage, GetTask]));

        var response = await RawAsync(app.Client, agent, Send(push: true));

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        Assert.Equal(0, await TaskCountAsync(agent));
        Assert.DoesNotContain(factory.TaskJobPublisher.PublishedMessages, message => message.AgentId == agent);
    }

    // (guarda 3.6) "O que o filtro autoriza é o que o SDK executa" (Q2, decisão do dono). Cada caso afirma o
    // que o SDK fez, não só o status. Defeito: o filtro com parser próprio, lendo o PRIMEIRO method.
    [Fact]
    public async Task WhatTheFilterAllows_IsWhatTheSdkRuns()
    {
        var agent = await AgentAsync();
        var reader = await ApplicationAsync((agent, [SendMessage, GetTask]));
        var own = TaskIdOf(await RpcAsync(reader.Client, agent, Send()));

        // GetTask e depois ListTasks: o SDK lê o último, ListTasks, e a aplicação não o tem.
        var listLast = await RawAsync(reader.Client, agent,
            $$"""{"jsonrpc":"2.0","id":1,"method":"GetTask","params":{"id":"{{own}}"},"method":"ListTasks"}""");
        Assert.Equal(HttpStatusCode.Forbidden, listLast.Status);
        Assert.Empty(listLast.Body);

        // ListTasks e depois GetTask: o SDK roda o GetTask, e a resposta é a task.
        var getLast = await RawAsync(reader.Client, agent,
            $$"""{"jsonrpc":"2.0","id":1,"method":"ListTasks","params":{"id":"{{own}}"},"method":"GetTask"}""");
        Assert.Equal(HttpStatusCode.OK, getLast.Status);
        Assert.Equal(own, JsonDocument.Parse(getLast.Body).RootElement.GetProperty("result").GetProperty("id").GetString());

        // A caixa trocada não é método A2A.
        Assert.Equal(HttpStatusCode.Forbidden, (await RawAsync(reader.Client, agent, Call("gettask", $$"""{"id":"{{own}}"}"""))).Status);

        // Push fora de configuration: o SDK não o lê dali, e o job vai sem push.
        var misplaced = TaskIdOf(await RpcAsync(reader.Client, agent, """
            {"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"role":"ROLE_USER","parts":[{"text":"Olá"}],"messageId":"MESSAGE_ID","pushNotificationConfig":{"url":"https://cliente.example.com/webhooks/a2a"}}}}
            """.Replace("MESSAGE_ID", Guid.NewGuid().ToString("N"))));
        Assert.Null(factory.TaskJobPublisher.PublishedMessages.Single(message => message.TaskId == misplaced).PushNotificationConfig);

        // pushNotificationConfig duplicado em configuration: o último vale, para o filtro e para o SDK.
        var duplicatedLastNull = TaskIdOf(await RpcAsync(reader.Client, agent, """
            {"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"role":"ROLE_USER","parts":[{"text":"Olá"}],"messageId":"MESSAGE_ID"},"configuration":{"pushNotificationConfig":{"url":"https://cliente.example.com/webhooks/a2a"},"pushNotificationConfig":null}}}
            """.Replace("MESSAGE_ID", Guid.NewGuid().ToString("N"))));
        Assert.Null(factory.TaskJobPublisher.PublishedMessages.Single(message => message.TaskId == duplicatedLastNull).PushNotificationConfig);
        var before = await TaskCountAsync(agent);
        var duplicatedLastPush = await RawAsync(reader.Client, agent, """
            {"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"role":"ROLE_USER","parts":[{"text":"Olá"}],"messageId":"MESSAGE_ID"},"configuration":{"pushNotificationConfig":null,"pushNotificationConfig":{"url":"https://cliente.example.com/webhooks/a2a"}}}}
            """.Replace("MESSAGE_ID", Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Forbidden, duplicatedLastPush.Status);
        Assert.Equal(before, await TaskCountAsync(agent));
    }

    // "Agente inativo com vínculo rejeita a task": passa no filtro e termina rejected, como para o inbox.
    [Fact]
    public async Task AnInactiveLinkedAgent_RejectsTheTask_WithNoJob()
    {
        var agent = await AgentAsync();
        var app = await ApplicationAsync((agent, [SendMessage]));
        (await (await factory.AdminClientAsync()).PostAsync($"/agents/{agent}/deactivate", content: null)).EnsureSuccessStatusCode();

        var body = await RpcAsync(app.Client, agent, Send());

        Assert.Equal("TASK_STATE_REJECTED", body.GetProperty("result").GetProperty("task").GetProperty("status").GetProperty("state").GetString());
        Assert.DoesNotContain(factory.TaskJobPublisher.PublishedMessages, message => message.AgentId == agent);
        Assert.True(await factory.WithDbAsync(db => db.TaskRejections.AnyAsync(rejection => rejection.AgentId == agent && rejection.Reason == "AgentInactive")));
    }

    // "O inbox não passa pela checagem": um agente que nenhuma aplicação tem liberado.
    [Fact]
    public async Task TheInbox_DoesNotGoThroughTheCheck()
    {
        var agent = await AgentAsync();

        var body = await RpcAsync(await InboxClientAsync(), agent, Send());

        Assert.Equal("TASK_STATE_SUBMITTED", body.GetProperty("result").GetProperty("task").GetProperty("status").GetProperty("state").GetString());
        Assert.Contains(factory.TaskJobPublisher.PublishedMessages, message => message.AgentId == agent);
    }

    // (guarda 4.2) "Dono gravado na criação" e "A task do inbox fica sem dono". Defeito: o store sem o dono
    // de escopo.
    [Fact]
    public async Task TheApplicationIsTheOwner_AndTheInboxTaskHasNone()
    {
        var agent = await AgentAsync();
        var app = await ApplicationAsync((agent, [SendMessage]));

        var appTask = TaskIdOf(await RpcAsync(app.Client, agent, Send()));
        var inboxTask = TaskIdOf(await RpcAsync(await InboxClientAsync(), agent, Send()));

        Assert.Equal(app.Id, await OwnerAsync(appTask));
        Assert.Null(await OwnerAsync(inboxTask));
    }

    // (guarda 4.4) Task de outra aplicação, task sem dono e task própria em outro agente: a resposta é a
    // de uma task que não existe, com o id que a própria aplicação mandou, byte a byte; a continuação
    // alheia não muda a task nem publica job. Defeitos: (a) sem a condição de agente; (b) sem a de dono.
    [Fact]
    public async Task SomeoneElsesTask_AnOwnerlessTask_AndAnOwnTaskOnAnotherAgent_AreNotFound()
    {
        var agent = await AgentAsync();
        var otherAgent = await AgentAsync();
        var first = await ApplicationAsync((agent, [SendMessage, GetTask]));
        var second = await ApplicationAsync((agent, [SendMessage, GetTask]), (otherAgent, [SendMessage, GetTask]));
        var firstTask = TaskIdOf(await RpcAsync(first.Client, agent, Send()));
        var inboxTask = TaskIdOf(await RpcAsync(await InboxClientAsync(), agent, Send()));
        var ownOnOther = TaskIdOf(await RpcAsync(second.Client, otherAgent, Send()));
        var missingId = Guid.NewGuid().ToString("N");
        var missing = await RawAsync(second.Client, agent, Get(missingId));

        foreach (var taskId in new[] { firstTask, inboxTask, ownOnOther })
        {
            var response = await RawAsync(second.Client, agent, Get(taskId));
            Assert.Equal(missing.Status, response.Status);
            Assert.Equal(missing.ContentType, response.ContentType);
            Assert.Equal(Encoding.UTF8.GetString(missing.Body).Replace(missingId, taskId), Encoding.UTF8.GetString(response.Body));
        }

        var published = factory.TaskJobPublisher.PublishedMessages.Count;
        var stateBefore = await StateAsync(firstTask);
        var continuation = await RawAsync(second.Client, agent, Send(taskId: firstTask));
        Assert.Equal(
            Encoding.UTF8.GetString((await RawAsync(second.Client, agent, Send(taskId: missingId))).Body).Replace(missingId, firstTask),
            Encoding.UTF8.GetString(continuation.Body));
        Assert.Equal(stateBefore, await StateAsync(firstTask));
        Assert.Equal(published, factory.TaskJobPublisher.PublishedMessages.Count);
    }

    // 5.2, "Filha num agente não liberado" (a2a-task-lifecycle), a parte da rota. A filha da delegação é
    // criada pelo apps/workers sem dono (o acordo está no rt-crossapp, TaskOwnerCompatibilityTests); aqui ela
    // é uma task sem dono no agente B, criada pelo inbox, que é a mesma linha. A aplicação, liberada só no
    // agente A, não a alcança: pela rota de A é a task inexistente, e pela de B, o 404 do agente sem vínculo.
    [Fact]
    public async Task AChildTaskOnAnUnlinkedAgent_IsNotReachable()
    {
        var parentAgent = await AgentAsync();
        var childAgent = await AgentAsync();
        var app = await ApplicationAsync((parentAgent, [SendMessage, GetTask]));
        var child = TaskIdOf(await RpcAsync(await InboxClientAsync(), childAgent, Send()));
        Assert.Null(await OwnerAsync(child));

        var throughParent = await RpcAsync(app.Client, parentAgent, Get(child));
        var throughChild = await RawAsync(app.Client, childAgent, Get(child));

        Assert.Equal(-32001, throughParent.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, throughChild.Status);
    }

    // (guarda 4.5) "O inbox lê a task de uma aplicação". Defeito: o filtro de dono aplicado a todo subject.
    [Fact]
    public async Task TheInbox_ReadsAnApplicationsTask()
    {
        var agent = await AgentAsync();
        var app = await ApplicationAsync((agent, [SendMessage]));
        var taskId = TaskIdOf(await RpcAsync(app.Client, agent, Send()));

        var body = await RpcAsync(await InboxClientAsync(), agent, Get(taskId));

        Assert.Equal(taskId, body.GetProperty("result").GetProperty("id").GetString());
    }

    // (guarda 4.7) "Contexto inexistente e contexto alheio dão a mesma resposta" (Q5, decisão do dono), e a
    // conversa própria continua. Defeitos: (a) só o contexto de outro dono recusado (o desconhecido passa
    // como conversa nova); (b) a mensagem do alheio diferente da do inexistente.
    [Fact]
    public async Task AnUnknownContext_AndSomeoneElsesContext_GetTheSameAnswer()
    {
        var agent = await AgentAsync();
        var app = await ApplicationAsync((agent, [SendMessage, GetTask]));
        var inboxContext = ContextIdOf(await RpcAsync(await InboxClientAsync(), agent, Send()));
        var before = await TaskCountAsync(agent);
        var published = factory.TaskJobPublisher.PublishedMessages.Count;

        var unknown = await RawAsync(app.Client, agent, Send(contextId: Guid.NewGuid().ToString("N")));
        var foreign = await RawAsync(app.Client, agent, Send(contextId: inboxContext));

        Assert.Equal(unknown, foreign);
        var error = JsonDocument.Parse(unknown.Body).RootElement.GetProperty("error");
        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Equal("Contexto não encontrado.", error.GetProperty("message").GetString());
        Assert.Equal(before, await TaskCountAsync(agent));
        Assert.Equal(published, factory.TaskJobPublisher.PublishedMessages.Count);

        // "Conversa nova sem contexto" e "A conversa continua no contexto próprio".
        var first = await RpcAsync(app.Client, agent, Send());
        var context = ContextIdOf(first);
        var next = await RpcAsync(app.Client, agent, Send(contextId: context));
        Assert.Equal(context, ContextIdOf(next));
        Assert.Equal(context, factory.TaskJobPublisher.PublishedMessages.Single(message => message.TaskId == TaskIdOf(next)).ContextId);
    }

    // 7.3: de ponta a ponta, sem semeador — o Admin cria e libera pelas rotas, a aplicação pede o token,
    // envia e consulta no agente liberado, e recebe 404 no outro.
    [Fact]
    public async Task EndToEnd_AnApplicationReachesOnlyItsLinkedAgent()
    {
        var linked = await AgentAsync();
        var other = await AgentAsync();
        var app = await ApplicationAsync((linked, [SendMessage, GetTask]));

        var taskId = TaskIdOf(await RpcAsync(app.Client, linked, Send()));
        var read = await RpcAsync(app.Client, linked, Get(taskId));

        Assert.Equal(taskId, read.GetProperty("result").GetProperty("id").GetString());
        Assert.Equal(app.Id, await OwnerAsync(taskId));
        Assert.Equal(HttpStatusCode.NotFound, (await RawAsync(app.Client, other, Send())).Status);
    }

    // ---- passos ----

    private sealed record App(Guid Id, HttpClient Client);

    /// <summary>A resposta inteira, para comparar byte a byte (o record compara o corpo pelo conteúdo).</summary>
    private sealed record Raw(HttpStatusCode Status, string? ContentType, long? ContentLength, byte[] Body)
    {
        public bool Equals(Raw? other) =>
            other is not null && Status == other.Status && ContentType == other.ContentType &&
            ContentLength == other.ContentLength && Body.AsSpan().SequenceEqual(other.Body);

        public override int GetHashCode() => HashCode.Combine(Status, ContentType, Body.Length);

        public override string ToString() => $"{(int)Status} {ContentType} {ContentLength} '{Encoding.UTF8.GetString(Body)}'";
    }

    private async Task<Guid> AgentAsync()
    {
        var response = await (await factory.AdminClientAsync()).PostAsJsonAsync(
            "/agents/",
            new { name = $"Agente {Guid.NewGuid():N}"[..20], instructions = "Responda.", provider = "openai", model = "gpt-5.6-sol" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<App> ApplicationAsync(params (Guid Agent, string[] Operations)[] grants)
    {
        var created = await factory.CreateAsync();
        var admin = await factory.AdminClientAsync();
        foreach (var (agent, operations) in grants)
        {
            var response = await admin.PostAsJsonAsync($"/applications/{created.Id}/agents", new { agenteId = agent, operacoes = operations });
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        }

        return new App(created.Id, factory.WithToken(await factory.AccessTokenAsync(created.ClientId, created.Secret)));
    }

    private async Task DeactivateAsync(Guid applicationId) =>
        (await (await factory.AdminClientAsync()).PostAsync($"/applications/{applicationId}/deactivate", content: null)).EnsureSuccessStatusCode();

    private async Task<HttpClient> InboxClientAsync()
    {
        await ServiceClientTokens.EnsureAsync(factory.Services);
        return factory.WithToken(await ServiceClientTokens.AccessTokenAsync(factory, IdentityClients.InboxServiceClientId));
    }

    private static string Send(bool push = false, string? contextId = null, string? taskId = null)
    {
        var message = new Dictionary<string, object?>
        {
            ["role"] = "ROLE_USER",
            ["parts"] = new[] { new { text = "Olá" } },
            ["messageId"] = Guid.NewGuid().ToString("N"),
        };
        if (contextId is not null)
        {
            message["contextId"] = contextId;
        }

        if (taskId is not null)
        {
            message["taskId"] = taskId;
        }

        var parameters = new Dictionary<string, object?> { ["message"] = message };
        if (push)
        {
            parameters["configuration"] = new { pushNotificationConfig = new { url = "https://cliente.example.com/webhooks/a2a" } };
        }

        return JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "SendMessage", @params = parameters });
    }

    private static string Get(string taskId) => Call("GetTask", $$"""{"id":"{{taskId}}"}""");

    private static string Call(string method, string parameters) =>
        $$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{parameters}}}""";

    private static async Task<Raw> RawAsync(HttpClient client, Guid agentId, string json)
    {
        var response = await client.PostAsync($"/agents/{agentId}/a2a", new StringContent(json, Encoding.UTF8, "application/json"));
        return new Raw(
            response.StatusCode,
            response.Content.Headers.ContentType?.ToString(),
            response.Content.Headers.ContentLength,
            await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>A resposta JSON-RPC de uma chamada que tem de passar no filtro (HTTP 200).</summary>
    private static async Task<JsonElement> RpcAsync(HttpClient client, Guid agentId, string json)
    {
        var raw = await RawAsync(client, agentId, json);
        Assert.True(raw.Status == HttpStatusCode.OK, raw.ToString());
        return JsonDocument.Parse(raw.Body).RootElement.Clone();
    }

    private static string TaskIdOf(JsonElement sendResponse)
    {
        Assert.False(sendResponse.TryGetProperty("error", out var error), error.ToString());
        return sendResponse.GetProperty("result").GetProperty("task").GetProperty("id").GetString()!;
    }

    private static string ContextIdOf(JsonElement sendResponse) =>
        sendResponse.GetProperty("result").GetProperty("task").GetProperty("contextId").GetString()!;

    private Task<int> TaskCountAsync(Guid agentId) =>
        factory.WithDbAsync(db => db.A2ATasks.CountAsync(task => task.AgentId == agentId));

    private Task<Guid?> OwnerAsync(string taskId) =>
        factory.WithDbAsync(db => db.A2ATasks.Where(task => task.TaskId == taskId).Select(task => task.ApplicationId).SingleAsync());

    private Task<string> StateAsync(string taskId) =>
        factory.WithDbAsync(db => db.A2ATasks.Where(task => task.TaskId == taskId).Select(task => task.State).SingleAsync());
}
