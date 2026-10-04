using System.Net;
using Buteco.Connectors.Tests.Support;
using Xunit.Abstractions;

namespace Buteco.Connectors.Tests;

// google-drive-connector, "Nada da chave sai do processo além do e-mail". Captura
// logs (todas as categorias), corpos de resposta e mensagens de exceção, e procura a
// chave neles. A precondição no fim de cada coleta impede o teste de passar por
// vacuidade: sem texto capturado, não há o que procurar.
public class SecretLeakTests(ITestOutputHelper output)
{
    // knowledge-sync-cycle, "Nenhum log com conteúdo, token ou chave" (design.md da change
    // ciclo-de-sincronizacao, D13). Pela composição real, com os logs de todas as
    // categorias, inclusive os do HttpClient que chama o apps/api.
    [Fact]
    public async Task NenhumLogDoCicloContemConteudoTokenChaveOuNomeDeArquivo()
    {
        const string ContentMarker = "CONTEUDO-MARCADOR-7f3a91";
        const string FileName = "Relatorio-Confidencial-Nome-9c2e.md";
        await using var factory = new ConnectorsFactory(
            extraConfiguration: new Dictionary<string, string?> { ["Api:BaseUrl"] = "http://api.test" },
            configureServices: services =>
            {
                var scheduler = services.SingleOrDefault(descriptor => descriptor.ImplementationType == typeof(Buteco.Connectors.Sync.SyncSchedulerService));
                if (scheduler is not null)
                {
                    services.Remove(scheduler);
                }
            });
        var knowledgeBase = factory.SyncApi.AddBase();
        var recorded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.SyncApi.Override = (request, body) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/sync-results", StringComparison.Ordinal))
            {
                recorded.TrySetResult();
            }

            // Uma recusa e uma contenção, para os caminhos de aviso também logarem.
            return request.Method == HttpMethod.Put && body!.Contains("ref-grande", StringComparison.Ordinal)
                ? FakeSyncApiHandler.ContentRefusal("too-large", 2_000_000)
                : request.Method == HttpMethod.Put && body!.Contains("ref-contencao", StringComparison.Ordinal)
                    ? FakeSyncApiHandler.Contention()
                    : null;
        };
        factory.Fake.ListRoot = _ => new Buteco.Connectors.Connectors.RootListing(
            [
                new("ref-ok", FileName, "v1", "text/markdown"),
                new("ref-grande", FileName + ".grande", "v1", "text/markdown"),
                new("ref-contencao", FileName + ".contencao", "v1", "text/markdown"),
            ],
            [new("ref-atalho", FileName + ".atalho", "shortcut-not-followed", null)]);
        factory.Fake.Markdown = _ => $"# {ContentMarker}\n\nTexto {ContentMarker}.\n";

        var response = await TestAuthentication.CreateClientAs(factory, "operator")
            .PostAsync($"/connectors/knowledge-bases/{knowledgeBase.Id}/sync", null);
        await recorded.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var sentTokens = factory.SyncApi.Requests
            .Select(request => request.Authorization!["Bearer ".Length..])
            .ToList();
        var lines = factory.Logs.Lines.ToList();

        // Precondição contra vacuidade: houve tokens enviados e o ciclo logou.
        Assert.NotEmpty(sentTokens);
        Assert.Contains(lines, line => line.Contains(knowledgeBase.Id.ToString(), StringComparison.Ordinal));
        Assert.Contains(factory.SyncApi.Upserts(knowledgeBase.Id), request => request.Body!.Contains(ContentMarker, StringComparison.Ordinal));
        foreach (var line in lines)
        {
            Assert.DoesNotContain(ContentMarker, line, StringComparison.Ordinal);
            Assert.DoesNotContain(FileName, line, StringComparison.Ordinal);
            Assert.DoesNotContain(TestAuthentication.TokenSigningKey, line, StringComparison.Ordinal);
            Assert.All(sentTokens, token => Assert.DoesNotContain(token, line, StringComparison.Ordinal));
        }

        output.WriteLine($"{lines.Count} linhas de log, {sentTokens.Count} tokens procurados.");
    }

    [Fact]
    public async Task NenhumaSaidaContemAChave()
    {
        var key = TestServiceAccountKey.Create();
        var invalidKey = TestServiceAccountKey.Create(fields => fields["type"] = "authorized_user");
        var captured = new List<string>();
        var statuses = new HashSet<HttpStatusCode>();

        // 1. Boot com chave válida, e as rotas em sucesso, 422, 502 e 503.
        await using (var factory = new ConnectorsFactory(googleKeyBase64: key.Base64))
        {
            factory.Google.OnPath("/drive/v3/drives", _ => GoogleDriveFakeHandler.Json(HttpStatusCode.OK, """{"drives":[{"id":"d1","name":"Equipe"}]}"""));
            factory.Google.OnList("sharedWithMe = true", _ => GoogleDriveFakeHandler.Json(HttpStatusCode.OK, """{"files":[]}"""));
            factory.Google.OnPath("/drive/v3/files/pasta-ok", _ => GoogleDriveFakeHandler.Fixture("pasta-principal.json"));
            factory.Google.OnPath("/drive/v3/files/sem-acesso", _ => GoogleDriveTestKit.NotFound("sem-acesso"));
            factory.Google.OnPath("/drive/v3/files/api-desligada", _ => GoogleDriveFakeHandler.GoogleError(
                HttpStatusCode.Forbidden, "accessNotConfigured", "Google Drive API has not been used in project 000000000000 before or it is disabled.", "usageLimits"));
            factory.Google.OnPath("/drive/v3/files/cota", _ => GoogleDriveFakeHandler.GoogleError(
                HttpStatusCode.TooManyRequests, "rateLimitExceeded", "Rate Limit Exceeded", "usageLimits"));

            var operatorClient = TestAuthentication.CreateClientAs(factory, "operator");
            var apiClient = TestAuthentication.CreateClientAs(factory, "service:api");
            foreach (var (client, route) in new[]
            {
                (operatorClient, "/connectors/providers"),
                (operatorClient, "/connectors/providers/google-drive/folders"),
                (apiClient, "/connectors/providers/google-drive/folder?id=pasta-ok"),
                (apiClient, "/connectors/providers/google-drive/folder?id=sem-acesso"),
                (apiClient, "/connectors/providers/google-drive/folder?id=api-desligada"),
                (operatorClient, "/connectors/providers/google-drive/folders?parentId=cota"),
            })
            {
                var response = await client.GetAsync(route);
                statuses.Add(response.StatusCode);
                captured.Add(await response.Content.ReadAsStringAsync());
            }

            captured.AddRange(factory.Logs.Lines);
            Assert.NotEmpty(factory.Google.IssuedAccessTokens);
            await AssertNoLeak("rotas", captured, key, factory.Google.IssuedAccessTokens);
        }

        Assert.Superset(new HashSet<HttpStatusCode> { HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadGateway, HttpStatusCode.ServiceUnavailable }, statuses);

        // 2. Troca de token recusada pelo Google.
        await using (var factory = new ConnectorsFactory(googleKeyBase64: key.Base64))
        {
            factory.Google.On(r => r.RequestUri!.AbsoluteUri == GoogleDriveFakeHandler.TokenUrl, _ =>
                GoogleDriveFakeHandler.Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Invalid JWT Signature."}"""));
            var client = TestAuthentication.CreateClientAs(factory, "operator");

            var response = await client.GetAsync("/connectors/providers/google-drive/folders");
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

            var texts = new List<string> { await response.Content.ReadAsStringAsync() };
            texts.AddRange(factory.Logs.Lines);
            texts.AddRange(factory.Google.Requests.Where(r => r.Uri.Host != "oauth2.googleapis.com").Select(r => r.Uri.ToString()));
            await AssertNoLeak("token recusado", texts, key, []);
        }

        // 3. Boot com chave inválida que carrega uma chave privada de verdade.
        using (var factory = new ConnectorsFactory(googleKeyBase64: invalidKey.Base64))
        {
            var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
            var texts = new List<string> { exception.ToString(), RouteAuthenticationTests.Flatten(exception) };
            texts.AddRange(factory.Logs.Lines);
            await AssertNoLeak("boot inválido", texts, invalidKey, []);
        }
    }

    private Task AssertNoLeak(string stage, IReadOnlyCollection<string> captured, TestServiceAccountKey key, IEnumerable<string> accessTokens)
    {
        // Precondição contra vacuidade: há texto capturado, e ele não é só vazio.
        Assert.True(captured.Count >= 2, $"capturou só {captured.Count} textos");
        Assert.True(captured.Sum(text => text.Length) > 200, "texto capturado curto demais para a busca valer");
        output.WriteLine($"{stage}: {captured.Count} textos, {captured.Sum(text => text.Length)} caracteres examinados");

        var middle = key.Base64.Length / 2;
        var needles = new List<(string What, string Value)>
        {
            ("trecho do corpo do PEM", key.PemBodyFragment),
            ("private_key_id", key.PrivateKeyId),
            ("base64 inteiro", key.Base64),
            ("trecho do meio do base64", key.Base64.Substring(middle, 40)),
        };
        needles.AddRange(accessTokens.Select(token => ("access_token", token)));

        foreach (var text in captured)
        {
            foreach (var (what, value) in needles)
            {
                Assert.False(text.Contains(value, StringComparison.Ordinal), $"vazou {what} em: {text[..Math.Min(text.Length, 200)]}");
            }
        }

        return Task.CompletedTask;
    }
}
