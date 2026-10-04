using System.Net;
using System.Text.Json;
using Buteco.Connectors.Sync;
using Buteco.Connectors.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Buteco.Connectors.Tests;

/// <summary>
/// "Sincronizar agora" (design.md da change ciclo-de-sincronizacao, D9): a confirmação da
/// base no apps/api, os códigos de falha e o lock por base. O agendamento sai da
/// composição destes testes, para nenhuma rodada periódica concorrer com o pedido.
/// </summary>
public class SyncEndpointsTests
{
    private static readonly TimeSpan SafetyLimit = TimeSpan.FromSeconds(30);

    private static string SyncNow(Guid knowledgeBaseId) => $"/connectors/knowledge-bases/{knowledgeBaseId}/sync";

    private static ConnectorsFactory Factory(string? apiBaseUrl = "http://api.test") =>
        new(
            extraConfiguration: new Dictionary<string, string?> { ["Api:BaseUrl"] = apiBaseUrl ?? string.Empty },
            configureServices: services =>
            {
                var scheduler = services.SingleOrDefault(descriptor => descriptor.ImplementationType == typeof(SyncSchedulerService));
                if (scheduler is not null)
                {
                    services.Remove(scheduler);
                }
            });

    /// <summary>Sinaliza a gravação do desfecho de uma base, sem mudar a resposta.</summary>
    private static TaskCompletionSource ResultRecorded(ConnectorsFactory factory, Guid knowledgeBaseId)
    {
        var recorded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.SyncApi.Override = (request, _) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith($"{knowledgeBaseId}/sync-results", StringComparison.Ordinal))
            {
                recorded.TrySetResult();
            }

            return null;
        };
        return recorded;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Operator_SyncsTheBase_202_AndTheCycleRecordsTheResult(bool isActive)
    {
        await using var factory = Factory();
        var knowledgeBase = factory.SyncApi.AddBase(isActive: isActive);
        var recorded = ResultRecorded(factory, knowledgeBase.Id);
        var client = TestAuthentication.CreateClientAs(factory, "operator");

        var response = await client.PostAsync(SyncNow(knowledgeBase.Id), null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await recorded.Task.WaitAsync(SafetyLimit);
        Assert.Equal("Succeeded", Assert.Single(factory.SyncApi.Results(knowledgeBase.Id)).GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task BaseNotInTheList_Is404KnowledgeBaseNotFound()
    {
        await using var factory = Factory();
        var client = TestAuthentication.CreateClientAs(factory, "operator");

        var response = await client.PostAsync(SyncNow(Guid.NewGuid()), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("knowledge-base-not-found", await CodeOf(response));
    }

    [Fact]
    public async Task WithoutApiBaseUrl_Is503SyncNotConfigured_WithoutAnyCall()
    {
        await using var factory = Factory(apiBaseUrl: null);
        var client = TestAuthentication.CreateClientAs(factory, "operator");

        var response = await client.PostAsync(SyncNow(Guid.NewGuid()), null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("sync-not-configured", await CodeOf(response));
        Assert.Empty(factory.SyncApi.Requests);
    }

    [Fact]
    public async Task ApiUnreachable_Is503SyncApiUnavailable()
    {
        await using var factory = Factory();
        factory.SyncApi.Override = (_, _) => throw new HttpRequestException("recusada");
        var client = TestAuthentication.CreateClientAs(factory, "operator");

        var response = await client.PostAsync(SyncNow(Guid.NewGuid()), null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("sync-api-unavailable", await CodeOf(response));
    }

    // Repassar o 401 do apps/api deslogaria o operador no painel.
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ApiOutOfContract_Is502SyncApiError_NeverTheSameStatus(HttpStatusCode apiStatus)
    {
        await using var factory = Factory();
        factory.SyncApi.Override = (_, _) => new HttpResponseMessage(apiStatus);
        var client = TestAuthentication.CreateClientAs(factory, "operator");

        var response = await client.PostAsync(SyncNow(Guid.NewGuid()), null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("sync-api-error", await CodeOf(response));
    }

    // A barreira segura o primeiro ciclo na listagem até o segundo pedido chegar. Se o
    // teste passasse sem a barreira ter sido alcançada, ele não provaria nada: por isso
    // a chegada à barreira é afirmada antes do segundo pedido.
    [Fact]
    public async Task TwoSimultaneousRequests_RunOneCycle()
    {
        await using var factory = Factory();
        var knowledgeBase = factory.SyncApi.AddBase();
        var recorded = ResultRecorded(factory, knowledgeBase.Id);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Fake.ListRootGate = async () =>
        {
            reached.TrySetResult();
            await release.Task;
        };
        var client = TestAuthentication.CreateClientAs(factory, "operator");

        var first = await client.PostAsync(SyncNow(knowledgeBase.Id), null);
        await reached.Task.WaitAsync(SafetyLimit);
        Assert.True(reached.Task.IsCompletedSuccessfully, "O primeiro ciclo não chegou à barreira.");
        var second = await client.PostAsync(SyncNow(knowledgeBase.Id), null);
        release.TrySetResult();
        await recorded.Task.WaitAsync(SafetyLimit);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(1, factory.Fake.ListRootCalls);
        Assert.Single(factory.SyncApi.Results(knowledgeBase.Id));
    }

    [Fact]
    public async Task TheCycleLock_IsReleasedAfterTheCycle()
    {
        await using var factory = Factory();
        var knowledgeBase = factory.SyncApi.AddBase();
        var recorded = ResultRecorded(factory, knowledgeBase.Id);
        var client = TestAuthentication.CreateClientAs(factory, "operator");
        await client.PostAsync(SyncNow(knowledgeBase.Id), null);
        await recorded.Task.WaitAsync(SafetyLimit);

        // O lock sai no finally do ciclo, depois da gravação: espera o lock, não o tempo.
        var inProgress = factory.Services.GetRequiredService<SyncInProgress>();
        await WaitUntilAsync(() => inProgress.TryStart(knowledgeBase.Id));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var limit = new CancellationTokenSource(SafetyLimit);
        while (!condition())
        {
            await Task.Yield();
            limit.Token.ThrowIfCancellationRequested();
        }
    }
}
