using System.Net;
using System.Net.Http.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeDocuments.Entities;
using Buteco.Api.KnowledgeDocuments.Requests;
using Buteco.Api.KnowledgeDocuments.Responses;
using Buteco.Api.KnowledgeSync.Responses;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// <c>ContentRevision</c> como token de concorrência (design.md da change
/// catalogo-base-sincronizada, D10): o SQL que o EF emite, e a segunda falha
/// seguida, que não pode virar 500. <c>partial</c> da classe existente (D12).
/// </summary>
public partial class KnowledgeDocumentCatalogTests
{
    /// <summary>
    /// O <c>UPDATE</c> e o <c>DELETE</c> emitidos levam a revisão lida no
    /// <c>WHERE</c>. Afirmado sobre o SQL que o EF realmente emitiu no caminho da
    /// requisição (convenção 11), e não de memória.
    /// </summary>
    [Fact]
    public async Task DocumentUpdateAndDelete_CarryTheReadRevisionInTheWhereClause()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base SQL do token");
        var document = await _client.CreateDocumentAsync(knowledgeBase.Id, "Doc SQL do token");
        var path = $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}";

        var updateCommands = await factory.SqlCapture.CaptureAsync(async () =>
            (await _client.PutAsJsonAsync(path, new UpdateKnowledgeDocumentRequest("Doc SQL do token", "markdown", "# Outro\n"))).EnsureSuccessStatusCode());
        var deleteCommands = await factory.SqlCapture.CaptureAsync(async () =>
            (await _client.DeleteAsync(path)).EnsureSuccessStatusCode());

        var update = EmittedSqlCapture.SingleCommandContaining(updateCommands, "UPDATE knowledge_documents");
        var delete = EmittedSqlCapture.SingleCommandContaining(deleteCommands, "DELETE FROM knowledge_documents");
        Console.WriteLine($"[SQL do token]\n{update}\n---\n{delete}");
        Assert.Matches("""WHERE "Id" = @\w+ AND "ContentRevision" = @\w+""", update);
        Assert.Matches("""WHERE "Id" = @\w+ AND "ContentRevision" = @\w+""", delete);
    }

    /// <summary>
    /// Duas falhas de concorrência seguidas, forçadas de forma determinística: um
    /// interceptor lança <see cref="DbUpdateConcurrencyException"/> em todo
    /// <c>SaveChanges</c> que grava um documento existente, num host derivado da
    /// fixture (mesmo banco, mesmo duplo de publicação). Cada caminho de escrita relê
    /// uma vez, falha de novo, e responde 503 com <c>Retry-After</c>, nunca 500, sem
    /// gravar documento, sem registrar evento e sem publicar indexação — medido
    /// caminho a caminho, antes e depois de cada requisição.
    /// </summary>
    [Fact]
    public async Task SecondConsecutiveConcurrencyFailure_Returns503WithRetryAfter_OnEveryWritePath()
    {
        var manual = await _client.CreateBaseAsync("Base manual falha dupla");
        var manualDocument = await _client.CreateDocumentAsync(manual.Id, "Doc falha dupla");
        var synced = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        var syncedDocumentId = (await Connectors.UpsertOkAsync(synced.Id, "ref-falha-dupla", "v1")).DocumentId;
        var syncedBefore = (await FindByRefAsync(synced.Id, "ref-falha-dupla"))!;

        await using var failing = WithFailingDocumentWrites(new AlwaysConcurrencyConflictInterceptor());
        var operatorClient = failing.CreateClient();
        var connectorsClient = KnowledgeSyncTestSeed.CreateConnectorsClient(failing);
        var manualPath = $"/knowledge-bases/{manual.Id}/documents/{manualDocument.Id}";

        // O host derivado publica no MESMO duplo que a fixture observa; sem isso a
        // asserção de "nenhuma publicação" passaria por vacuidade.
        Assert.Same(
            factory.IndexingPublisher,
            failing.Services.GetRequiredService<Buteco.Api.Knowledge.Indexing.IKnowledgeIndexingJobPublisher>());

        var paths = new (string Name, Guid KnowledgeBaseId, Guid DocumentId, Func<Task<HttpResponseMessage>> Send)[]
        {
            ("PUT operador", manual.Id, manualDocument.Id, () => operatorClient.PutAsJsonAsync(
                manualPath, new UpdateKnowledgeDocumentRequest("Doc falha dupla", "markdown", "# Outro\n"))),
            ("reindexar", manual.Id, manualDocument.Id, () => operatorClient.PostAsync($"{manualPath}/reindex", null)),
            ("DELETE operador", manual.Id, manualDocument.Id, () => operatorClient.DeleteAsync(manualPath)),
            ("upsert", synced.Id, syncedDocumentId, () => connectorsClient.UpsertAsync(
                synced.Id, "ref-falha-dupla", "v2", content: "# Outro\n")),
            ("DELETE por referência", synced.Id, syncedDocumentId, () => connectorsClient.DeleteAsync(
                $"{KnowledgeSyncTestSeed.DocumentsPath(synced.Id)}?externalRef=ref-falha-dupla")),
        };

        foreach (var (name, knowledgeBaseId, documentId, send) in paths)
        {
            var eventsBefore = await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, knowledgeBaseId);
            var publishedBefore = factory.IndexingPublisher.PublishedFor(documentId).Count;

            var response = await send();

            Assert.True(response.StatusCode == HttpStatusCode.ServiceUnavailable, $"{name}: {(int)response.StatusCode}");
            Assert.Equal("1", response.Headers.RetryAfter?.ToString());
            Assert.True(
                eventsBefore == await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, knowledgeBaseId),
                $"{name}: registrou evento no histórico");
            Assert.True(
                publishedBefore == factory.IndexingPublisher.PublishedFor(documentId).Count,
                $"{name}: publicou indexação");
        }

        // Nada gravado: os dois documentos continuam como estavam.
        var stillThere = (await _client.GetFromJsonAsync<KnowledgeDocumentResponse>(manualPath))!;
        Assert.Equal(manualDocument.ContentRevision, stillThere.ContentRevision);
        Assert.Equal(manualDocument.ExtractedText, stillThere.ExtractedText);
        var syncedAfter = (await FindByRefAsync(synced.Id, "ref-falha-dupla"))!;
        Assert.Equal(syncedBefore.ExtractedText, syncedAfter.ExtractedText);
        Assert.Equal(syncedBefore.ContentHash, syncedAfter.ContentHash);
        Assert.Equal(syncedBefore.ContentRevision, syncedAfter.ContentRevision);
        Assert.Equal("v1", syncedAfter.ExternalVersion);
    }

    /// <summary>
    /// O upsert perde a primeira gravação e o documento deixa de existir antes da
    /// releitura (D10): a rota responde 503 com <c>Retry-After</c>, em vez de decidir
    /// por conta própria recriar o documento. Forçado de forma determinística por um
    /// interceptor que, na primeira gravação do documento, o exclui por SQL numa
    /// conexão própria e lança <see cref="DbUpdateConcurrencyException"/>. Repetir o
    /// mesmo upsert, sem o interceptor, cria o documento: é a razão de o 503 existir.
    /// </summary>
    [Fact]
    public async Task Upsert_WhenTheDocumentIsDeletedBeforeTheReread_Returns503AndARetryCreatesIt()
    {
        var synced = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        var documentId = (await Connectors.UpsertOkAsync(synced.Id, "ref-excluido-no-meio", "v1")).DocumentId;
        var interceptor = new DeleteThenConflictOnceInterceptor(ConnectionStringWithPassword(), documentId);
        var eventsBefore = await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, synced.Id);
        var publishedBefore = factory.IndexingPublisher.Published.Count;

        await using (var failing = WithFailingDocumentWrites(interceptor))
        {
            var response = await KnowledgeSyncTestSeed.CreateConnectorsClient(failing)
                .UpsertAsync(synced.Id, "ref-excluido-no-meio", "v2", content: "# Texto novo\n");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("1", response.Headers.RetryAfter?.ToString());
        }

        // Precondição do arranjo: o interceptor disparou, uma vez.
        Assert.Equal(1, interceptor.Fired);
        Assert.Equal(0, await CountByRefAsync(synced.Id, "ref-excluido-no-meio"));
        Assert.Equal(eventsBefore, await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, synced.Id));
        Assert.Equal(publishedBefore, factory.IndexingPublisher.Published.Count);

        var retried = await Connectors.UpsertOkAsync(synced.Id, "ref-excluido-no-meio", "v2", content: "# Texto novo\n");

        Assert.Equal(SyncedDocumentUpsertOutcome.Created, retried.Outcome);
        Assert.Equal(1, await CountByRefAsync(synced.Id, "ref-excluido-no-meio"));
    }

    /// <summary>
    /// A string de conexão original, com a senha, vem das opções que a fixture
    /// registrou; <c>GetConnectionString()</c> devolveria a string sem ela.
    /// </summary>
    private string ConnectionStringWithPassword()
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>()
            .FindExtension<NpgsqlOptionsExtension>()!.ConnectionString!;
    }

    private WebApplicationFactory<Program> WithFailingDocumentWrites(IInterceptor interceptor)
    {
        var connectionString = ConnectionStringWithPassword();

        return factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options
                .UseButecoAgentsNpgsql(connectionString)
                .AddInterceptors(interceptor));
        }));
    }

    /// <summary>
    /// Na primeira gravação que modifica o documento indicado, exclui esse documento
    /// por SQL numa conexão própria e lança <see cref="DbUpdateConcurrencyException"/>,
    /// que é o estado em que o handler vai reler. As gravações seguintes passam.
    /// </summary>
    private sealed class DeleteThenConflictOnceInterceptor(string connectionString, Guid documentId) : SaveChangesInterceptor
    {
        private int _fired;

        public int Fired => Volatile.Read(ref _fired);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var target = eventData.Context!.ChangeTracker.Entries<KnowledgeDocument>()
                .Where(entry => entry.State == EntityState.Modified && entry.Entity.Id == documentId)
                .ToList();

            if (target.Count > 0 && Interlocked.Exchange(ref _fired, 1) == 0)
            {
                await using var connection = new Npgsql.NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = new Npgsql.NpgsqlCommand(
                    $"DELETE FROM knowledge_documents WHERE \"Id\" = '{documentId}';", connection);
                await command.ExecuteNonQueryAsync(cancellationToken);

                throw new DbUpdateConcurrencyException(
                    "Conflito forçado pelo teste, com o documento excluído antes da releitura.",
                    target.Select(entry => (Microsoft.EntityFrameworkCore.Update.IUpdateEntry)entry.GetInfrastructure()).ToList());
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>
    /// Lança <see cref="DbUpdateConcurrencyException"/> antes de qualquer comando
    /// sempre que o <c>SaveChanges</c> modifica ou exclui um documento — é a forma da
    /// falha que o EF produz quando o <c>UPDATE</c> não acha a revisão lida.
    /// Inclusão passa, para o arranjo poder criar o que precisa.
    /// </summary>
    private sealed class AlwaysConcurrencyConflictInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var conflicting = eventData.Context!.ChangeTracker.Entries<KnowledgeDocument>()
                .Where(entry => entry.State is EntityState.Modified or EntityState.Deleted)
                .Cast<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry>()
                .ToList();

            if (conflicting.Count > 0)
            {
                throw new DbUpdateConcurrencyException("Conflito forçado pelo teste.", conflicting.Select(entry => (Microsoft.EntityFrameworkCore.Update.IUpdateEntry)entry.GetInfrastructure()).ToList());
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
