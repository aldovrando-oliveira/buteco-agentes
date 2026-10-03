using System.Net;
using System.Text;
using Buteco.Api.KnowledgeSync.Connectors;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Buteco.Api.Tests.KnowledgeSync;

/// <summary>
/// O cliente do <c>apps/connectors</c> contra um <c>HttpClient</c> com handler falso,
/// um caso por linha das tabelas da D2 e da D3 do design.md da change
/// criacao-base-sincronizada. Sem contêiner. Os corpos de erro seguem o que o
/// <c>apps/connectors</c> de fato devolve (tarefa 1.2): <c>ProblemDetails</c> com a
/// extensão <c>code</c>, e a chave <c>detail</c> AUSENTE quando não há detalhe.
/// </summary>
public class ConnectorsFolderClientTests
{
    private const string BaseUrl = "http://connectors.test";

    public static TheoryData<string, string?, HttpStatusCode, int> ConnectorFailures() => new()
    {
        { "access-denied", "conta@conectores.test", HttpStatusCode.UnprocessableEntity, 422 },
        { "not-a-folder", null, HttpStatusCode.UnprocessableEntity, 422 },
        { "folder-trashed", null, HttpStatusCode.UnprocessableEntity, 422 },
        { "provider-not-configured", null, HttpStatusCode.NotFound, 422 },
        { "api-not-configured", null, HttpStatusCode.BadGateway, 502 },
        { "provider-auth-failed", null, HttpStatusCode.BadGateway, 502 },
        { "provider-error", "bad-request", HttpStatusCode.BadGateway, 502 },
        { "rate-limited", null, HttpStatusCode.ServiceUnavailable, 503 },
        { "provider-unavailable", null, HttpStatusCode.ServiceUnavailable, 503 },
        // Código desconhecido no formato passa: o conjunto é do apps/connectors (D3).
        { "folder-too-deep", null, HttpStatusCode.UnprocessableEntity, 422 },
    };

    [Theory]
    [MemberData(nameof(ConnectorFailures))]
    public async Task ConnectorFailure_PassesCodeAndDetailWithStatusByNature(
        string code, string? detail, HttpStatusCode connectorsStatus, int expectedStatus)
    {
        var client = Client(_ => Problem(connectorsStatus, code, detail));

        var result = await client.DescribeFolderAsync("fake", "pasta-1", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedStatus, result.FailureStatus);
        Assert.Equal(code, result.FailureCode);
        Assert.Equal(detail, result.FailureDetail);
    }

    [Fact]
    public async Task Success_ReturnsTheDescribedFolder()
    {
        var client = Client(_ => Json(HttpStatusCode.OK, """{"id":"pasta-1","name":"Atendimento","webUrl":"https://drive.test/pasta-1"}"""));

        var result = await client.DescribeFolderAsync("fake", "pasta-1", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(new ConnectorsFolderDescription("pasta-1", "Atendimento", "https://drive.test/pasta-1"), result.Folder);
    }

    public static TheoryData<string, string> OutOfContractOk() => new()
    {
        { "sem name", """{"id":"pasta-1","webUrl":"https://drive.test/pasta-1"}""" },
        { "name vazio", """{"id":"pasta-1","name":"","webUrl":"https://drive.test/pasta-1"}""" },
        { "sem webUrl", """{"id":"pasta-1","name":"Atendimento"}""" },
        { "webUrl relativa", """{"id":"pasta-1","name":"Atendimento","webUrl":"/pasta-1"}""" },
        { "webUrl ftp", """{"id":"pasta-1","name":"Atendimento","webUrl":"ftp://drive.test/pasta-1"}""" },
        { "id diferente", """{"id":"outra-pasta","name":"Atendimento","webUrl":"https://drive.test/pasta-1"}""" },
        { "id em outra caixa", """{"id":"PASTA-1","name":"Atendimento","webUrl":"https://drive.test/pasta-1"}""" },
        { "não é JSON", "<html>ok</html>" },
        { "lista", "[]" },
    };

    [Theory]
    [MemberData(nameof(OutOfContractOk))]
    public async Task OkOutOfContract_IsConnectorsError502(string _, string body)
    {
        var client = Client(_ => Json(HttpStatusCode.OK, body));

        var result = await client.DescribeFolderAsync("fake", "pasta-1", CancellationToken.None);

        AssertFailure(result, 502, ConnectorsFailureCodes.Error, "200");
    }

    public static TheoryData<HttpStatusCode, string> OutOfContractErrors() => new()
    {
        // 400 do apps/connectors (id ausente) não tem code.
        { HttpStatusCode.BadRequest, """{"title":"Informe o id da pasta.","status":400}""" },
        // 401 e 403 são chave divergente ou subject fora da tabela: nunca repassados,
        // porque o painel desloga o operador em 401 (D2).
        { HttpStatusCode.Unauthorized, "" },
        { HttpStatusCode.Forbidden, "" },
        // 403 COM code no formato também não passa: 403 é só autorização (D9 da #103).
        { HttpStatusCode.Forbidden, """{"code":"access-denied","status":403}""" },
        { HttpStatusCode.InternalServerError, """{"title":"An error occurred while processing your request.","status":500}""" },
        { HttpStatusCode.NotFound, "" },
        { HttpStatusCode.UnprocessableEntity, """{"status":422}""" },
        // Frase no lugar do código não passa, e não aparece em lugar nenhum do resultado.
        { HttpStatusCode.UnprocessableEntity, """{"code":"Sem acesso","status":422}""" },
        { HttpStatusCode.UnprocessableEntity, """{"code":"access-denied\n","status":422}""" },
        { HttpStatusCode.BadGateway, "<html>Bad Gateway</html>" },
    };

    [Theory]
    [MemberData(nameof(OutOfContractErrors))]
    public async Task ErrorOutOfContract_IsConnectorsError502WithReceivedStatus(HttpStatusCode status, string body)
    {
        var client = Client(_ => Json(status, body));

        var result = await client.DescribeFolderAsync("fake", "pasta-1", CancellationToken.None);

        AssertFailure(result, 502, ConnectorsFailureCodes.Error, ((int)status).ToString());
        Assert.DoesNotContain("Sem acesso", $"{result.FailureCode} {result.FailureDetail}", StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectionRefused_IsConnectorsUnavailable503()
    {
        var client = Client(_ => throw new HttpRequestException("Connection refused"));

        var result = await client.DescribeFolderAsync("fake", "pasta-1", CancellationToken.None);

        AssertFailure(result, 503, ConnectorsFailureCodes.Unavailable, null);
    }

    [Fact]
    public async Task Timeout_IsConnectorsUnavailable503()
    {
        var client = Client(
            async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                return Json(HttpStatusCode.OK, "{}");
            },
            timeout: TimeSpan.FromMilliseconds(100));

        var result = await client.DescribeFolderAsync("fake", "pasta-1", CancellationToken.None);

        AssertFailure(result, 503, ConnectorsFailureCodes.Unavailable, null);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        var client = Client(async (_, cancellationToken) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return Json(HttpStatusCode.OK, "{}");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.DescribeFolderAsync("fake", "pasta-1", cancellation.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task NotConfigured_IsConnectorsNotConfigured503WithoutAnyRequest(string? baseUrl)
    {
        var requests = 0;
        var client = Client(_ =>
        {
            requests++;
            return Json(HttpStatusCode.OK, "{}");
        }, baseUrl: baseUrl);

        var result = await client.DescribeFolderAsync("fake", "pasta-1", CancellationToken.None);

        AssertFailure(result, 503, ConnectorsFailureCodes.NotConfigured, null);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task Request_EscapesProviderAndFolderId()
    {
        Uri? requested = null;
        HttpMethod? method = null;
        var client = Client(request =>
        {
            requested = request.RequestUri;
            method = request.Method;
            return Json(HttpStatusCode.OK, """{"id":"a/b?c=d&e","name":"X","webUrl":"https://drive.test/x"}""");
        });

        var result = await client.DescribeFolderAsync("google-drive", "a/b?c=d&e", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("/connectors/providers/google-drive/folder", requested!.AbsolutePath);
        Assert.Equal("?id=a%2Fb%3Fc%3Dd%26e", requested.Query);
    }

    private static void AssertFailure(ConnectorsFolderResult result, int status, string code, string? detail)
    {
        Assert.False(result.Succeeded);
        Assert.Null(result.Folder);
        Assert.Equal(status, result.FailureStatus);
        Assert.Equal(code, result.FailureCode);
        Assert.Equal(detail, result.FailureDetail);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // Forma do erro do apps/connectors (ConnectorEndpoints.Failure): detail omitido
    // quando nulo.
    private static HttpResponseMessage Problem(HttpStatusCode status, string code, string? detail)
    {
        var detailPart = detail is null ? "" : $",\"detail\":\"{detail}\"";
        var response = Json(status, $"{{\"type\":\"https://tools.ietf.org/html/rfc9110\",\"title\":\"x\",\"status\":{(int)status}{detailPart},\"code\":\"{code}\"}}");
        response.Content.Headers.ContentType = new("application/problem+json");
        return response;
    }

    private static ConnectorsFolderClient Client(
        Func<HttpRequestMessage, HttpResponseMessage> respond, string? baseUrl = BaseUrl) =>
        Client((request, _) => Task.FromResult(respond(request)), baseUrl: baseUrl);

    private static ConnectorsFolderClient Client(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        TimeSpan? timeout = null,
        string? baseUrl = BaseUrl)
    {
        var factory = new SingleClientFactory(new HttpClient(new DelegateHandler(respond))
        {
            BaseAddress = string.IsNullOrWhiteSpace(baseUrl) ? null : new Uri(baseUrl),
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
        });

        return new ConnectorsFolderClient(
            factory,
            Microsoft.Extensions.Options.Options.Create(new ConnectorsOptions { BaseUrl = baseUrl }),
            NullLogger<ConnectorsFolderClient>.Instance);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(ConnectorsFolderClient.HttpClientName, name);
            return client;
        }
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
