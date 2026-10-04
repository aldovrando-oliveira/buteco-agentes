using System.Net;
using Buteco.Api.Infrastructure;
using Buteco.Api.Knowledge.Indexing;
using Buteco.Api.KnowledgeSync.Responses;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// Escritas de <c>/sync</c> contra a exclusão da base (design.md da change
/// exclusao-base-conhecimento, D6). <c>partial</c> da classe existente: nenhuma fonte
/// de contêiner nova (D12).
///
/// <para>
/// A janela é provocada de forma determinística: um <see cref="SaveChangesInterceptor"/>
/// num host derivado da fixture (mesmo Postgres) chama a rota REAL
/// <c>DELETE /knowledge-bases/{id}</c>, pela fixture, no primeiro <c>SaveChanges</c> da
/// escrita observada, e só depois deixa a gravação seguir. A escrita leu a base
/// existindo e grava com ela excluída. O interceptor conta o disparo: escrita que não
/// viu a janela não prova nada.
/// </para>
/// </summary>
public partial class KnowledgeDocumentCatalogTests
{
    private const string EventsInsert = "INSERT INTO knowledge_document_events";

    /// <summary>
    /// Exclui a base pela rota real no primeiro <c>SaveChanges</c> e guarda o status que a
    /// exclusão respondeu; as gravações seguintes passam.
    /// </summary>
    private sealed class DeleteBaseOnFirstSaveInterceptor(HttpClient operatorClient, Guid knowledgeBaseId) : SaveChangesInterceptor
    {
        private int _fired;

        public int Fired => Volatile.Read(ref _fired);

        public HttpStatusCode? DeletionStatus { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
            {
                var response = await operatorClient.DeleteAsync($"/knowledge-bases/{knowledgeBaseId}", cancellationToken);
                DeletionStatus = response.StatusCode;
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private async Task<int> CountDocumentsOfBaseAsync(Guid knowledgeBaseId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.KnowledgeDocuments.CountAsync(document => document.KnowledgeBaseId == knowledgeBaseId);
    }

    // --- 5.8 ---------------------------------------------------------------------

    /// <summary>
    /// Cada escrita de <c>/sync</c> lê a base existindo e grava com ela excluída: responde
    /// 404, nunca 500 nem 503 nem 204, e não deixa documento, evento nem indexação. Uma
    /// base sincronizada inativa por caminho, porque cada caminho a exclui.
    /// </summary>
    [Fact]
    public async Task SyncWrites_WhenTheBaseIsDeletedBetweenReadAndSave_Return404()
    {
        var operatorClient = factory.CreateClient();
        Assert.Same(factory.IndexingPublisher, factory.Services.GetRequiredService<IKnowledgeIndexingJobPublisher>());

        var paths = new (string Name, string? ExistingRef, Func<HttpClient, Guid, Task<HttpResponseMessage>> Send)[]
        {
            ("upsert de inclusão", null, (client, id) => client.UpsertAsync(id, "ref-nova-na-janela", "v1")),
            ("upsert de atualização", "ref-na-janela", (client, id) => client.UpsertAsync(id, "ref-na-janela", "v2", content: "# Texto novo\n")),
            ("upsert sem mudança de documento", "ref-na-janela", (client, id) => client.UpsertAsync(id, "ref-na-janela", "v2")),
            ("exclusão por referência", "ref-na-janela", (client, id) => client.DeleteAsync($"{KnowledgeSyncTestSeed.DocumentsPath(id)}?externalRef=ref-na-janela")),
            ("resultado de ciclo", null, (client, id) => client.RecordFailureAsync(id)),
        };

        var failures = new List<string>();
        foreach (var (name, existingRef, send) in paths)
        {
            var synced = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services, $"Base da janela: {name}", isActive: false);
            if (existingRef is not null)
            {
                await Connectors.UpsertOkAsync(synced.Id, existingRef, "v1");
            }

            var publishedBefore = factory.IndexingPublisher.Published.Count;
            var interceptor = new DeleteBaseOnFirstSaveInterceptor(operatorClient, synced.Id);

            HttpResponseMessage response;
            await using (var windowed = WithFailingDocumentWrites(interceptor))
            {
                response = await send(KnowledgeSyncTestSeed.CreateConnectorsClient(windowed), synced.Id);
            }

            // Todos os caminhos rodam antes de qualquer asserção, para a falha dizer o que
            // cada um respondeu, e não só o primeiro.
            var documents = await CountDocumentsOfBaseAsync(synced.Id);
            var events = await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, synced.Id);
            var published = factory.IndexingPublisher.Published.Count - publishedBefore;
            Console.WriteLine(
                $"[janela] {name}: {(int)response.StatusCode}, interceptor {interceptor.Fired}, exclusão {(int?)interceptor.DeletionStatus}");

            if (interceptor.Fired != 1)
            {
                failures.Add($"{name}: o interceptor disparou {interceptor.Fired} vez(es) — a escrita não viu a janela");
            }

            if (interceptor.DeletionStatus != HttpStatusCode.NoContent)
            {
                failures.Add($"{name}: a exclusão respondeu {(int?)interceptor.DeletionStatus}");
            }

            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                failures.Add($"{name}: respondeu {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            }

            if (documents != 0 || events != 0 || published != 0)
            {
                failures.Add($"{name}: {documents} documento(s), {events} evento(s), {published} publicação(ões) depois da janela");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    // --- 5.9 ---------------------------------------------------------------------

    [Fact]
    public async Task SyncRoutes_AfterTheBaseIsDeleted_Return404()
    {
        var synced = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services, "Base sync excluída antes", isActive: false);
        await Connectors.UpsertOkAsync(synced.Id, "ref-antes", "v1");
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/knowledge-bases/{synced.Id}")).StatusCode);

        var responses = new (string Name, HttpResponseMessage Response)[]
        {
            ("listagem de referências", await Connectors.GetAsync(KnowledgeSyncTestSeed.DocumentsPath(synced.Id))),
            ("upsert", await Connectors.UpsertAsync(synced.Id, "ref-antes", "v2")),
            ("exclusão por referência", await Connectors.DeleteAsync($"{KnowledgeSyncTestSeed.DocumentsPath(synced.Id)}?externalRef=ref-antes")),
            ("resultado de ciclo", await Connectors.RecordFailureAsync(synced.Id)),
        };

        foreach (var (name, response) in responses)
        {
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{name}: {(int)response.StatusCode}");
        }

        Assert.Equal(0, await CountDocumentsOfBaseAsync(synced.Id));
    }

    // --- D6: a ordem medida na 3.1, afirmada ---------------------------------------

    /// <summary>
    /// O impasse entre uma escrita de <c>/sync</c> e a exclusão da base não é alcançável
    /// porque o EF emite o <c>INSERT</c> do evento (que faz <c>FOR KEY SHARE</c> na base)
    /// ANTES do comando sobre a linha do documento — a mesma ordem da exclusão, base e
    /// depois documentos (design.md, D6, medido na tarefa 3.1). A ordem dos comandos
    /// do EF não é contrato: uma versão que a inverta reprova aqui, e é este teste que
    /// reabre a questão do impasse.
    /// </summary>
    [Fact]
    public async Task SyncDocumentWrites_EmitTheEventInsertBeforeTouchingTheDocumentRow()
    {
        var synced = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services, "Base da ordem dos comandos");
        await Connectors.UpsertOkAsync(synced.Id, "ref-ordem", "v1");

        var update = await factory.SqlCapture.CaptureAsync(async () =>
            (await Connectors.UpsertAsync(synced.Id, "ref-ordem", "v2", content: "# Outro texto\n")).EnsureSuccessStatusCode());
        var delete = await factory.SqlCapture.CaptureAsync(async () =>
            (await Connectors.DeleteAsync($"{KnowledgeSyncTestSeed.DocumentsPath(synced.Id)}?externalRef=ref-ordem")).EnsureSuccessStatusCode());

        foreach (var (commands, documentCommand) in new[] { (update, "UPDATE knowledge_documents"), (delete, "DELETE FROM knowledge_documents") })
        {
            var command = EmittedSqlCapture.SingleCommandContaining(commands, EventsInsert, documentCommand);
            Assert.True(
                command.IndexOf(EventsInsert, StringComparison.Ordinal) < command.IndexOf(documentCommand, StringComparison.Ordinal),
                $"O evento não vem antes de [{documentCommand}]; o impasse com a exclusão da base passa a ser alcançável:\n{command}");
        }
    }

    // --- D6: 40P01 tratado como a concorrência -------------------------------------

    /// <summary>
    /// Lança, uma vez, a forma que o EF dá a um impasse no <c>SaveChanges</c>: uma
    /// <see cref="DbUpdateException"/> com <see cref="PostgresException"/> de
    /// <c>40P01</c> por dentro.
    /// </summary>
    private sealed class DeadlockOnceInterceptor : SaveChangesInterceptor
    {
        private int _fired;

        public int Fired => Volatile.Read(ref _fired);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
            {
                throw new DbUpdateException(
                    "Impasse forçado pelo teste.",
                    new PostgresException("deadlock detected", "ERROR", "ERROR", PostgresErrorCodes.DeadlockDetected));
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>
    /// Com a base existindo, o impasse é contenção: o upsert limpa, relê e grava na
    /// tentativa única que já existe, em vez de responder 500. Exercitado com a exceção
    /// injetada, porque a ordem medida não produz impasse real (D6).
    /// </summary>
    [Fact]
    public async Task Upsert_AfterOneDeadlockWithTheBaseStillThere_RetriesAndSucceeds()
    {
        var synced = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services, "Base do impasse injetado");
        await Connectors.UpsertOkAsync(synced.Id, "ref-impasse", "v1");
        var interceptor = new DeadlockOnceInterceptor();

        HttpResponseMessage response;
        await using (var deadlocking = WithFailingDocumentWrites(interceptor))
        {
            response = await KnowledgeSyncTestSeed.CreateConnectorsClient(deadlocking)
                .UpsertAsync(synced.Id, "ref-impasse", "v2", content: "# Depois do impasse\n");
        }

        Assert.Equal(1, interceptor.Fired);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = (await FindByRefAsync(synced.Id, "ref-impasse"))!;
        Assert.Equal("v2", stored.ExternalVersion);
    }
}
