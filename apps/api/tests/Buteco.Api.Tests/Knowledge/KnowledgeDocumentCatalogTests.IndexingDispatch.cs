using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Knowledge.Indexing;
using Buteco.Api.KnowledgeDocuments.Requests;
using Buteco.Api.KnowledgeDocuments.Responses;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PublisherMode = Buteco.Api.Tests.Support.FakeKnowledgeIndexingJobPublisher.PublisherMode;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// O despacho dos pedidos de indexação (design.md da change indexacao-sem-job-orfao,
/// D3, D8 e os riscos R2, R3, R4, R6, R8 e R9). Os hosts derivados da fixture dividem
/// banco e duplo de publicação com ela, e cada um tem o seu despacho, a sua janela e
/// a sua varredura — que é como duas instâncias do <c>apps/api</c> se veem.
/// </summary>
public partial class KnowledgeDocumentCatalogTests
{
    // --- Exclusão (D1: FK em cascata) ----------------------------------------

    [Fact]
    public async Task DeletingTheDocumentOrTheBase_TakesThePendingRequestsAlong_AndNothingIsPublishedLater()
    {
        var manual = await _client.CreateBaseAsync("Base pedido excluído");
        var doomedBase = await _client.CreateBaseAsync("Base excluída com pedido");
        Guid documentId, baseDocumentId;
        using (PublicationFailing())
        {
            documentId = (await _client.CreateDocumentAsync(manual.Id, "Doc excluído com pedido")).Id;
            baseDocumentId = (await _client.CreateDocumentAsync(doomedBase.Id, "Doc da base excluída")).Id;
        }

        Assert.Equal(1, await CountRequestsAsync(documentId));
        Assert.Equal(1, await CountRequestsAsync(baseDocumentId));

        (await _client.DeleteAsync($"/knowledge-bases/{manual.Id}/documents/{documentId}")).EnsureSuccessStatusCode();
        (await _client.PostAsync($"/knowledge-bases/{doomedBase.Id}/deactivate", null)).EnsureSuccessStatusCode();
        (await _client.DeleteAsync($"/knowledge-bases/{doomedBase.Id}")).EnsureSuccessStatusCode();

        Assert.Equal(0, await CountRequestsAsync(documentId));
        Assert.Equal(0, await CountRequestsAsync(baseDocumentId));

        await RunSweepAsync();
        Assert.Empty(factory.IndexingPublisher.PublishedFor(documentId));
        Assert.Empty(factory.IndexingPublisher.PublishedFor(baseDocumentId));
    }

    // --- Atualização sem conteúdo novo não grava pedido ----------------------

    [Fact]
    public async Task TitleOnlyUpdate_WithPublicationFailing_RecordsNoRequest_AndPublishesNothingLater()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base só título fila fora");
        var document = await _client.CreateDocumentAsync(knowledgeBase.Id, "Doc só título fila fora");
        var attemptsBefore = factory.IndexingPublisher.Attempts;
        var publishedBefore = factory.IndexingPublisher.PublishedFor(document.Id).Count;

        HttpResponseMessage response;
        using (PublicationFailing())
        {
            response = await _client.PutAsJsonAsync(
                $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}",
                new UpdateKnowledgeDocumentRequest("Título novo", "markdown", KnowledgeTestClient.SampleMarkdown));
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await CountRequestsAsync(document.Id));
        Assert.Equal(attemptsBefore, factory.IndexingPublisher.Attempts);

        await RunSweepAsync();
        Assert.Equal(publishedBefore, factory.IndexingPublisher.PublishedFor(document.Id).Count);
    }

    // --- Revisões sucessivas (R6) --------------------------------------------

    [Fact]
    public async Task TwoContentUpdatesWhilePublicationFails_PublishBothRevisionsInOrder_WhenItRecovers()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base revisões sucessivas");
        var document = await _client.CreateDocumentAsync(knowledgeBase.Id, "Doc revisões sucessivas");
        var path = $"/knowledge-bases/{knowledgeBase.Id}/documents/{document.Id}";
        var publishedBefore = factory.IndexingPublisher.PublishedFor(document.Id).Count;

        using (PublicationFailing())
        {
            (await _client.PutAsJsonAsync(path, new UpdateKnowledgeDocumentRequest("Doc revisões sucessivas", "markdown", "# Revisão 2\n"))).EnsureSuccessStatusCode();
            (await _client.PutAsJsonAsync(path, new UpdateKnowledgeDocumentRequest("Doc revisões sucessivas", "markdown", "# Revisão 3\n"))).EnsureSuccessStatusCode();
        }

        Assert.Equal(2, await CountRequestsAsync(document.Id));

        await RunSweepAsync();

        var revisions = factory.IndexingPublisher.PublishedFor(document.Id).Skip(publishedBefore)
            .Select(message => message.ContentRevision).ToList();
        Assert.Equal(new[] { 2, 3 }, revisions);
        Assert.Equal(0, await CountRequestsAsync(document.Id));
    }

    // --- Pedido não confirmado continua (4.6) --------------------------------

    [Fact]
    public async Task SweepWhosePublicationFails_KeepsTheRequest_OpensTheWindow_AndTheNextCyclePublishesIt()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base pedido não confirmado");
        Guid documentId;
        using (PublicationFailing())
        {
            documentId = (await _client.CreateDocumentAsync(knowledgeBase.Id, "Doc não confirmado")).Id;

            var window = factory.Services.GetRequiredService<KnowledgeIndexingDispatchWindow>();
            window.Close();

            await RunSweepAsync();

            Assert.Equal(1, await CountRequestsAsync(documentId));
            Assert.Empty(factory.IndexingPublisher.PublishedFor(documentId));
            Assert.True(window.IsOpen, "falha de publicação na varredura não abriu a janela");
        }

        await RunSweepAsync();

        Assert.Single(factory.IndexingPublisher.PublishedFor(documentId));
        Assert.Equal(0, await CountRequestsAsync(documentId));
        Assert.False(factory.Services.GetRequiredService<KnowledgeIndexingDispatchWindow>().IsOpen);
    }

    // --- Várias instâncias (R2) ----------------------------------------------

    /// <summary>
    /// Dois despachos ao mesmo tempo — o da fixture e o de um host derivado, como duas
    /// instâncias — sobre os mesmos pedidos, com publicação lenta para que os dois
    /// estejam dentro da transação juntos. A corrida pode não acontecer numa execução
    /// (convenção 15, quinta forma), e por isso o par determinístico vem no teste
    /// seguinte, sobre o SQL emitido.
    /// </summary>
    [Fact]
    public async Task TwoConcurrentSweeps_PublishEachRequestExactlyOnce()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base duas instâncias");
        var documentIds = new List<Guid>();
        using (PublicationFailing())
        {
            for (var index = 0; index < 6; index++)
            {
                documentIds.Add((await _client.CreateDocumentAsync(knowledgeBase.Id, $"Doc duas instâncias {index}")).Id);
            }
        }

        await using var secondInstance = factory.WithWebHostBuilder(_ => { });
        factory.IndexingPublisher.Delay = TimeSpan.FromMilliseconds(150);
        try
        {
            await Task.WhenAll(
                RunSweepAsync(),
                secondInstance.Services.GetRequiredService<KnowledgeIndexingRequestDispatcher>().SweepOnceAsync(CancellationToken.None));
        }
        finally
        {
            factory.IndexingPublisher.Delay = TimeSpan.Zero;
        }

        foreach (var documentId in documentIds)
        {
            Assert.Single(factory.IndexingPublisher.PublishedFor(documentId));
            Assert.Equal(0, await CountRequestsAsync(documentId));
        }
    }

    [Fact]
    public async Task DispatchQueries_AreEmittedWithForUpdateSkipLocked()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base SQL do despacho");
        Guid documentId;
        using (PublicationFailing())
        {
            documentId = (await _client.CreateDocumentAsync(knowledgeBase.Id, "Doc SQL do despacho")).Id;
        }

        // A consulta da varredura e a do despacho no fim da escrita, cada uma do
        // caminho de produção.
        var sweepCommands = await factory.SqlCapture.CaptureAsync(() => RunSweepAsync());
        var writeCommands = await factory.SqlCapture.CaptureAsync(() => _client.CreateDocumentAsync(knowledgeBase.Id, "Doc SQL do despacho 2"));

        var sweep = EmittedSqlCapture.SingleCommandContaining(sweepCommands, "SELECT * FROM knowledge_indexing_requests", "LIMIT");
        var write = EmittedSqlCapture.SingleCommandContaining(writeCommands, "SELECT * FROM knowledge_indexing_requests", "ANY");
        Console.WriteLine($"[SQL do despacho]\n{sweep}\n---\n{write}");
        Assert.EndsWith("FOR UPDATE SKIP LOCKED", sweep.TrimEnd());
        Assert.EndsWith("FOR UPDATE SKIP LOCKED", write.TrimEnd());
        Assert.Single(factory.IndexingPublisher.PublishedFor(documentId));
    }

    // --- Varredura: falha de consulta, lote, serviço rodando sozinho (R4, R8) -

    [Fact]
    public async Task SweepQueryFailure_Throws_AndDoesNotOpenTheWindow()
    {
        var interceptor = new FailRequestQueryInterceptor(failures: int.MaxValue);
        await using var failing = WithDispatchHost(interceptor: interceptor);
        var window = failing.Services.GetRequiredService<KnowledgeIndexingDispatchWindow>();

        await Assert.ThrowsAnyAsync<DbException>(() =>
            failing.Services.GetRequiredService<KnowledgeIndexingRequestDispatcher>().SweepOnceAsync(CancellationToken.None));

        Assert.True(interceptor.Fired > 0);
        Assert.False(window.IsOpen, "falha da consulta da varredura abriu a janela");
    }

    /// <summary>
    /// O serviço rodando sozinho, sem ninguém chamar o despacho: o primeiro ciclo falha
    /// na consulta, e um ciclo seguinte publica o pedido (R4). É também o teste de que
    /// o <c>BackgroundService</c> está registrado e roda.
    /// </summary>
    [Fact]
    public async Task SweepService_SurvivesAFailedCycle_AndPublishesInALaterOne_WithoutAnyCall()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base varredura sozinha");
        Guid documentId;
        using (PublicationFailing())
        {
            documentId = (await _client.CreateDocumentAsync(knowledgeBase.Id, "Doc varredura sozinha")).Id;
        }

        var interceptor = new FailRequestQueryInterceptor(failures: 1);
        var failedCycles = new LogEventCounter(KnowledgeIndexingRequestSweepService.SweepCycleFailedEvent.Id);
        await using var running = WithDispatchHost(
            schedule: new KnowledgeIndexingRequestSchedule { SweepInterval = TimeSpan.FromMilliseconds(200) },
            interceptor: interceptor,
            logs: failedCycles);
        _ = running.Services; // sobe o host, e com ele a varredura

        var deadline = Stopwatch.StartNew();
        while (factory.IndexingPublisher.PublishedFor(documentId).Count == 0 && deadline.Elapsed < TimeSpan.FromSeconds(20))
        {
            await Task.Delay(100);
        }

        Assert.Equal(1, interceptor.Fired);
        Assert.Equal(1, failedCycles.Count);
        Assert.Single(factory.IndexingPublisher.PublishedFor(documentId));
        Assert.Equal(0, await CountRequestsAsync(documentId));
    }

    [Fact]
    public async Task SweepWithMoreRequestsThanTheBatch_PublishesAllInTheOrderTheyWereRecorded()
    {
        var knowledgeBase = await _client.CreateBaseAsync("Base lote pequeno");
        var documentIds = new List<Guid>();
        using (PublicationFailing())
        {
            for (var index = 0; index < 7; index++)
            {
                documentIds.Add((await _client.CreateDocumentAsync(knowledgeBase.Id, $"Doc lote {index}")).Id);
            }
        }

        await using var smallBatch = WithDispatchHost(
            schedule: new KnowledgeIndexingRequestSchedule { SweepInterval = TimeSpan.FromHours(1), BatchSize = 3 });
        var publishedBefore = factory.IndexingPublisher.Published.Count;

        await smallBatch.Services.GetRequiredService<KnowledgeIndexingRequestDispatcher>().SweepOnceAsync(CancellationToken.None);

        var order = factory.IndexingPublisher.Published.Skip(publishedBefore)
            .Select(message => message.KnowledgeDocumentId)
            .Where(documentIds.Contains)
            .ToList();
        Assert.Equal(documentIds, order);
    }

    // --- Limite do despacho na escrita (R3) ----------------------------------

    [Fact]
    public async Task WriteWithBlockingPublication_RespondsWithinTheDispatchLimit_WithTheRequestRecorded()
    {
        var limit = TimeSpan.FromSeconds(1);
        await using var host = WithDispatchHost(
            schedule: new KnowledgeIndexingRequestSchedule { SweepInterval = TimeSpan.FromHours(1), RequestDispatchTimeout = limit });
        var client = host.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync("Base limite do despacho");

        HttpResponseMessage response;
        var elapsed = Stopwatch.StartNew();
        factory.IndexingPublisher.Mode = PublisherMode.Blocking;
        try
        {
            response = await client.PostAsJsonAsync(
                $"/knowledge-bases/{knowledgeBase.Id}/documents",
                new CreateKnowledgeDocumentRequest("Doc limite do despacho", "markdown", KnowledgeTestClient.SampleMarkdown));
            elapsed.Stop();
        }
        finally
        {
            factory.IndexingPublisher.Mode = PublisherMode.Available;
        }

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var documentId = (await response.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>())!.Id;
        Assert.True(elapsed.Elapsed < limit + TimeSpan.FromSeconds(4), $"a escrita levou {elapsed.Elapsed}");
        Assert.Equal(1, await CountRequestsAsync(documentId));

        await host.Services.GetRequiredService<KnowledgeIndexingRequestDispatcher>().SweepOnceAsync(CancellationToken.None);
        Assert.Single(factory.IndexingPublisher.PublishedFor(documentId));
    }

    // --- Janela de D8 (R9) ---------------------------------------------------

    /// <summary>
    /// Provada pela <b>contagem</b>, não pelo relógio: com a publicação bloqueando,
    /// cinco escritas geram UMA tentativa de despacho vinda da requisição — a da
    /// primeira, que espera o limite e abre a janela. Sem a janela, seriam cinco. O
    /// tempo das escritas seguintes é asserção secundária, com folga larga (abaixo do
    /// limite). O limite é encurtado por DI só para a suíte não esperar 5 s.
    /// </summary>
    [Fact]
    public async Task AfterAFailedDispatch_FollowingWritesSkipIt_OneAttemptForFiveWrites_AndTheSweepPublishesAll()
    {
        var limit = TimeSpan.FromSeconds(2);
        await using var host = WithDispatchHost(
            schedule: new KnowledgeIndexingRequestSchedule { SweepInterval = TimeSpan.FromHours(1), RequestDispatchTimeout = limit });
        var operatorClient = host.CreateClient();
        var connectorsClient = KnowledgeSyncTestSeed.CreateConnectorsClient(host);
        var manual = await operatorClient.CreateBaseAsync("Base janela");
        var synced = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        var window = host.Services.GetRequiredService<KnowledgeIndexingDispatchWindow>();

        var documentIds = new List<Guid>();
        var followingWrites = new List<TimeSpan>();
        var attemptsBefore = factory.IndexingPublisher.Attempts;
        factory.IndexingPublisher.Mode = PublisherMode.Blocking;
        try
        {
            for (var index = 0; index < 5; index++)
            {
                var elapsed = Stopwatch.StartNew();
                Guid documentId;
                if (index % 2 == 0)
                {
                    var created = await operatorClient.PostAsJsonAsync(
                        $"/knowledge-bases/{manual.Id}/documents",
                        new CreateKnowledgeDocumentRequest($"Doc janela {index}", "markdown", KnowledgeTestClient.SampleMarkdown));
                    Assert.Equal(HttpStatusCode.Created, created.StatusCode);
                    documentId = (await created.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>())!.Id;
                }
                else
                {
                    var upserted = await connectorsClient.UpsertAsync(synced.Id, $"ref-janela-{index}");
                    Assert.Equal(HttpStatusCode.OK, upserted.StatusCode);
                    documentId = (await upserted.Content.ReadFromJsonAsync<Buteco.Api.KnowledgeSync.Responses.UpsertSyncedDocumentResponse>())!.DocumentId;
                }

                elapsed.Stop();
                documentIds.Add(documentId);
                if (index > 0)
                {
                    followingWrites.Add(elapsed.Elapsed);
                }
            }
        }
        finally
        {
            factory.IndexingPublisher.Mode = PublisherMode.Available;
        }

        var requestAttempts = factory.IndexingPublisher.Attempts - attemptsBefore;
        Assert.True(
            requestAttempts == 1,
            $"tentativas de despacho vindas da requisição em 5 escritas com a publicação bloqueando: {requestAttempts} (esperado 1)");
        Assert.Equal(4, window.Skipped);
        Assert.All(followingWrites, elapsed => Assert.True(elapsed < limit, $"escrita seguinte levou {elapsed}, limite {limit}"));
        foreach (var documentId in documentIds)
        {
            Assert.Equal(1, await CountRequestsAsync(documentId));
        }

        await host.Services.GetRequiredService<KnowledgeIndexingRequestDispatcher>().SweepOnceAsync(CancellationToken.None);

        foreach (var documentId in documentIds)
        {
            Assert.Single(factory.IndexingPublisher.PublishedFor(documentId));
            Assert.Equal(0, await CountRequestsAsync(documentId));
        }

        Assert.False(window.IsOpen, "a varredura que publicou não fechou a janela");
    }

    /// <summary>
    /// Host derivado da fixture (mesmo banco, mesmo duplo de publicação) com o seu
    /// agendamento e, opcionalmente, um interceptor de comando e um contador de log.
    /// </summary>
    private WebApplicationFactory<Program> WithDispatchHost(
        KnowledgeIndexingRequestSchedule? schedule = null,
        IInterceptor? interceptor = null,
        ILoggerProvider? logs = null)
    {
        var connectionString = ConnectionStringWithPassword();

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                if (schedule is not null)
                {
                    services.RemoveAll<KnowledgeIndexingRequestSchedule>();
                    services.AddSingleton(schedule);
                }

                if (interceptor is not null)
                {
                    services.RemoveAll<DbContextOptions<AppDbContext>>();
                    services.AddDbContext<AppDbContext>(options => options
                        .UseButecoAgentsNpgsql(connectionString)
                        .AddInterceptors(interceptor));
                }
            });

            if (logs is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(logs));
            }
        });
    }

    /// <summary>
    /// Falha as primeiras <paramref name="failures"/> consultas que tomam pedidos de
    /// indexação, como uma queda do banco na consulta da varredura.
    /// </summary>
    private sealed class FailRequestQueryInterceptor(int failures) : DbCommandInterceptor
    {
        private int _calls;
        private int _fired;

        /// <summary>Consultas que falharam de propósito.</summary>
        public int Fired => Volatile.Read(ref _fired);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM knowledge_indexing_requests", StringComparison.Ordinal) &&
                command.CommandText.Contains("SKIP LOCKED", StringComparison.Ordinal) &&
                Interlocked.Increment(ref _calls) <= failures)
            {
                Interlocked.Increment(ref _fired);
                throw new SimulatedDatabaseFailure();
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class SimulatedDatabaseFailure() : DbException("Falha simulada do banco na consulta da varredura.");
}
