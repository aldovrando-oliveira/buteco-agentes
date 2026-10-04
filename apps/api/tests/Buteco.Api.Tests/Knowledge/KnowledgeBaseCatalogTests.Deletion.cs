using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.AgentKnowledgeBindings.Requests;
using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Responses;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// Exclusão de base de conhecimento (design.md da change exclusao-base-conhecimento,
/// D2, D3, D5 e D11). <c>partial</c> da classe existente: nenhuma fonte de contêiner
/// nova (D12). O que some e o que fica é contado por SQL nas quatro tabelas que
/// pertencem à base, nunca inferido da resposta.
/// </summary>
public partial class KnowledgeBaseCatalogTests
{
    private sealed record BaseFootprint(int Documents, int Fragments, int Events, int Bindings);

    private async Task<BaseFootprint> FootprintAsync(Guid knowledgeBaseId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        async Task<int> CountAsync(string table) =>
            await dbContext.Database
                .SqlQueryRaw<int>($$"""SELECT count(*)::int AS "Value" FROM {{table}} WHERE "KnowledgeBaseId" = {0}""", knowledgeBaseId)
                .SingleAsync();

        return new BaseFootprint(
            await CountAsync("knowledge_documents"),
            await CountAsync("knowledge_fragments"),
            await CountAsync("knowledge_document_events"),
            await CountAsync("agent_knowledge_bases"));
    }

    private async Task<int> CountIndexingAttemptsAsync(Guid knowledgeBaseId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.Database
            .SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM knowledge_indexing_attempts WHERE "KnowledgeBaseId" = {0}""", knowledgeBaseId)
            .SingleAsync();
    }

    /// <summary>
    /// Um fragmento por documento, gravado por SQL: o <c>apps/api</c> não escreve
    /// fragmentos (quem escreve é o <c>apps/workers</c>), e a cascata documento →
    /// fragmento só é exercitada com a linha existindo.
    /// </summary>
    private async Task SeedFragmentAsync(Guid knowledgeBaseId, Guid documentId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var embedding = "[" + string.Join(',', Enumerable.Repeat("0.1", 4096)) + "]";
        await dbContext.Database.ExecuteSqlRawAsync($"""
            INSERT INTO knowledge_fragments
              ("Id", "KnowledgeDocumentId", "KnowledgeBaseId", "Ordinal", "Text", "Embedding",
               "EmbeddingProvider", "EmbeddingModel", "EmbeddingDimensions", "CreatedAt")
            VALUES ('{Guid.NewGuid()}', '{documentId}', '{knowledgeBaseId}', 0, 'trecho',
                    '{embedding}'::vector, 'openai', 'modelo', 4096, now());
            """);
    }

    private async Task SeedIndexingAttemptAsync(Guid knowledgeBaseId, Guid documentId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.ExecuteSqlAsync($"""
            INSERT INTO knowledge_indexing_attempts
                ("Id", "KnowledgeDocumentId", "KnowledgeBaseId", "ContentRevision", "Attempt",
                 "MaxAttempts", "StartedAt", "EndedAt", "Outcome", "FailurePhase", "FragmentCount")
            VALUES ({Guid.NewGuid()}, {documentId}, {knowledgeBaseId}, 1, 1, 3, now(), now(), 'Indexed', null, 1);
            """);
    }

    private async Task<Guid> CreateAgentLinkedToAsync(params Guid[] knowledgeBaseIds)
    {
        var created = await _client.PostAsJsonAsync(
            "/agents",
            new { name = "Agente da exclusão", instructions = "Instrução.", provider = "openai", model = "gpt-5.6-sol" });
        created.EnsureSuccessStatusCode();
        var agentId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var linked = await _client.PutAsJsonAsync(
            $"/agents/{agentId}/knowledge-bases", new ReplaceAgentKnowledgeBasesRequest(knowledgeBaseIds));
        linked.EnsureSuccessStatusCode();
        return agentId;
    }

    /// <summary>
    /// Base manual com dois documentos (um com fragmento e tentativa de indexação),
    /// os eventos de inclusão deles, e o agente informado vinculado. Desativada se
    /// <paramref name="deactivate"/>.
    /// </summary>
    private async Task<KnowledgeBaseResponse> SeedBaseWithContentAsync(string name, bool deactivate)
    {
        var knowledgeBase = await CreateBaseAsync(name);
        var first = await _client.CreateDocumentAsync(knowledgeBase.Id, $"{name} doc 1");
        await _client.CreateDocumentAsync(knowledgeBase.Id, $"{name} doc 2");
        await SeedFragmentAsync(knowledgeBase.Id, first.Id);
        await SeedIndexingAttemptAsync(knowledgeBase.Id, first.Id);

        if (deactivate)
        {
            (await _client.PostAsync($"/knowledge-bases/{knowledgeBase.Id}/deactivate", null)).EnsureSuccessStatusCode();
        }

        return knowledgeBase;
    }

    // --- 5.2 ---------------------------------------------------------------------

    [Fact]
    public async Task DeleteInactiveBase_WithContent_Returns204AndRemovesEverythingOfIt()
    {
        var doomed = await SeedBaseWithContentAsync("Base a excluir", deactivate: true);
        var kept = await CreateBaseAsync("Base que fica no agente");
        var agentId = await CreateAgentLinkedToAsync(doomed.Id, kept.Id);
        Assert.Equal(new BaseFootprint(2, 1, 2, 1), await FootprintAsync(doomed.Id));

        var response = await _client.DeleteAsync($"/knowledge-bases/{doomed.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/knowledge-bases/{doomed.Id}")).StatusCode);
        Assert.Equal(new BaseFootprint(0, 0, 0, 0), await FootprintAsync(doomed.Id));

        // O agente continua, só sem a base excluída (agent-knowledge-binding).
        var agent = await _client.GetFromJsonAsync<JsonElement>($"/agents/{agentId}");
        Assert.Equal("Agente da exclusão", agent.GetProperty("name").GetString());
        var linked = agent.GetProperty("knowledgeBases").EnumerateArray().Select(kb => kb.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([kept.Id], linked);
    }

    // --- 5.3 ---------------------------------------------------------------------

    [Fact]
    public async Task DeleteActiveBase_Returns409AndDeletesNothing()
    {
        var active = await SeedBaseWithContentAsync("Base ativa não se exclui", deactivate: false);
        await CreateAgentLinkedToAsync(active.Id);
        var before = await FootprintAsync(active.Id);
        Assert.Equal(new BaseFootprint(2, 1, 2, 1), before);

        var response = await _client.DeleteAsync($"/knowledge-bases/{active.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("knowledge-base-active", body.GetProperty("code").GetString());
        Assert.Contains("desativ", body.GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/knowledge-bases/{active.Id}")).StatusCode);
        Assert.Equal(before, await FootprintAsync(active.Id));
    }

    // --- 5.4 ---------------------------------------------------------------------

    [Fact]
    public async Task DeleteMissingBase_Returns404()
    {
        var response = await _client.DeleteAsync($"/knowledge-bases/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteBase_LeavesAnotherBaseIntact_AndKeepsIndexingMetrics()
    {
        var doomed = await SeedBaseWithContentAsync("Base excluída ao lado", deactivate: true);
        var neighbour = await SeedBaseWithContentAsync("Base vizinha intacta", deactivate: true);
        await CreateAgentLinkedToAsync(doomed.Id, neighbour.Id);
        var neighbourBefore = await FootprintAsync(neighbour.Id);
        Assert.Equal(new BaseFootprint(2, 1, 2, 1), neighbourBefore);
        Assert.Equal(1, await CountIndexingAttemptsAsync(doomed.Id));

        var response = await _client.DeleteAsync($"/knowledge-bases/{doomed.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(neighbourBefore, await FootprintAsync(neighbour.Id));
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/knowledge-bases/{neighbour.Id}")).StatusCode);
        // Métrica não tem FK para o catálogo e sobrevive a ele (historico-documentos-base, D3).
        Assert.Equal(1, await CountIndexingAttemptsAsync(doomed.Id));
    }

    // --- 5.5 ---------------------------------------------------------------------

    /// <summary>
    /// A pasta fica livre (D5). O primeiro lado — o mesmo cadastro respondendo 409
    /// antes da exclusão — é o que dá sentido ao 201 depois: sem ele, o 201 não
    /// provaria que a exclusão liberou nada.
    /// </summary>
    [Fact]
    public async Task DeleteSyncedBase_FreesTheFolderForANewBase()
    {
        await using var host = WithConnectors();
        var folderId = NewFolderId("pasta-liberada");
        var synced = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services, "Base dona da pasta", folderId, isActive: false);
        await KnowledgeSyncTestSeed.CreateConnectorsClient(factory).UpsertOkAsync(synced.Id, "ref-da-pasta");

        var whileOwned = await host.Client.PostAsJsonAsync("/knowledge-bases", SyncedRequest("Base nova na pasta", folderId));
        Assert.Equal(HttpStatusCode.Conflict, whileOwned.StatusCode);
        Assert.Equal("folder-in-use", (await BodyAsync(whileOwned)).GetProperty("code").GetString());

        var deleted = await _client.DeleteAsync($"/knowledge-bases/{synced.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(new BaseFootprint(0, 0, 0, 0), await FootprintAsync(synced.Id));

        var afterDeletion = await host.Client.PostAsJsonAsync("/knowledge-bases", SyncedRequest("Base nova na pasta", folderId));
        Assert.Equal(HttpStatusCode.Created, afterDeletion.StatusCode);
        var stored = Assert.Single(await BasesWithFolderAsync(folderId));
        Assert.Equal("Base nova na pasta", stored.Name);
    }

    // --- 5.6 ---------------------------------------------------------------------

    /// <summary>
    /// Par determinístico do bloqueio (D12, convenção 15 quinta forma): uma corrida
    /// entre exclusão e inclusão pode passar por sorte do escalonamento; o SQL e as
    /// transações que o EF registrou na requisição real não. Afirma, na ordem
    /// registrada: a transação começa, a linha da base é travada com
    /// <c>FOR UPDATE</c>, os documentos são apagados, a base é apagada, e só então
    /// a transação é confirmada, uma vez.
    /// </summary>
    [Fact]
    public async Task DeleteBase_LocksTheBaseRowBeforeDeletingDocuments_InOneTransaction()
    {
        var log = new CommandAndTransactionLog();
        await using var logged = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(log);
            logging.AddFilter(CommandAndTransactionLog.TransactionCategory, LogLevel.Debug);
        }));
        var client = logged.CreateClient();
        TestAuthentication.AttachOperatorToken(client, logged.Services);
        var knowledgeBase = await SeedBaseWithContentAsync("Base do SQL da exclusão", deactivate: true);

        log.Clear();
        var response = await client.DeleteAsync($"/knowledge-bases/{knowledgeBase.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var entries = log.Entries;
        int IndexOf(string fragment)
        {
            var index = entries.FindIndex(entry => entry.Contains(fragment, StringComparison.Ordinal));
            Assert.True(index >= 0, $"Nada contendo [{fragment}] foi registrado:\n{string.Join("\n---\n", entries)}");
            return index;
        }

        var began = IndexOf("Began transaction");
        var locked = IndexOf("FOR UPDATE");
        var documents = IndexOf("DELETE FROM knowledge_documents");
        var knowledgeBaseRow = IndexOf("DELETE FROM knowledge_bases");
        var committed = IndexOf("Committed transaction");

        Assert.True(
            began < locked && locked < documents && documents < knowledgeBaseRow && knowledgeBaseRow < committed,
            $"Ordem registrada: início {began}, FOR UPDATE {locked}, documentos {documents}, base {knowledgeBaseRow}, commit {committed}\n" +
            string.Join("\n---\n", entries));
        Assert.Contains("knowledge_bases", entries[locked], StringComparison.Ordinal);
        Assert.Single(entries, entry => entry.Contains("Committed transaction", StringComparison.Ordinal));
    }

    // --- 5.11 --------------------------------------------------------------------

    [Fact]
    public async Task DeleteBaseWithDocuments_RemovesItsEvents_AndRecordsNoNewEventAnywhere()
    {
        var doomed = await SeedBaseWithContentAsync("Base com histórico a excluir", deactivate: true);
        var other = await SeedBaseWithContentAsync("Base com histórico que fica", deactivate: true);
        var otherEventsBefore = await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, other.Id);
        Assert.Equal(2, await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, doomed.Id));

        var response = await _client.DeleteAsync($"/knowledge-bases/{doomed.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, doomed.Id));
        Assert.Equal(otherEventsBefore, await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, other.Id));
    }

    // --- D11: impasse na exclusão -------------------------------------------------

    /// <summary>
    /// Lança <c>40P01</c> no comando que apaga os documentos, nas primeiras
    /// <paramref name="times"/> vezes. A ordem medida não produz impasse real (D6); o
    /// caminho existe para que um impasse nunca vire 500, e é exercitado assim.
    /// </summary>
    private sealed class DeadlockOnDocumentsDeleteInterceptor(int times) : DbCommandInterceptor
    {
        private int _fired;

        public int Fired => Volatile.Read(ref _fired);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DELETE FROM knowledge_documents", StringComparison.Ordinal) &&
                Interlocked.Increment(ref _fired) <= times)
            {
                throw new PostgresException("deadlock detected", "ERROR", "ERROR", PostgresErrorCodes.DeadlockDetected);
            }

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private WebApplicationFactory<Program> WithInterceptor(IInterceptor interceptor)
    {
        string connectionString;
        using (var scope = factory.Services.CreateScope())
        {
            connectionString = scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>()
                .FindExtension<NpgsqlOptionsExtension>()!.ConnectionString!;
        }

        return factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options
                .UseButecoAgentsNpgsql(connectionString)
                .AddInterceptors(interceptor));
        }));
    }

    [Fact]
    public async Task DeleteBase_AfterOneDeadlock_RetriesAndDeletes()
    {
        var knowledgeBase = await SeedBaseWithContentAsync("Base do impasse único", deactivate: true);
        var interceptor = new DeadlockOnDocumentsDeleteInterceptor(times: 1);

        HttpResponseMessage response;
        await using (var deadlocking = WithInterceptor(interceptor))
        {
            var client = deadlocking.CreateClient();
            TestAuthentication.AttachOperatorToken(client, deadlocking.Services);
            response = await client.DeleteAsync($"/knowledge-bases/{knowledgeBase.Id}");
        }

        Assert.Equal(2, interceptor.Fired);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new BaseFootprint(0, 0, 0, 0), await FootprintAsync(knowledgeBase.Id));
    }

    [Fact]
    public async Task DeleteBase_AfterTwoDeadlocks_Returns503AndDeletesNothing()
    {
        var knowledgeBase = await SeedBaseWithContentAsync("Base do impasse duplo", deactivate: true);
        var before = await FootprintAsync(knowledgeBase.Id);
        var interceptor = new DeadlockOnDocumentsDeleteInterceptor(times: 2);

        HttpResponseMessage response;
        await using (var deadlocking = WithInterceptor(interceptor))
        {
            var client = deadlocking.CreateClient();
            TestAuthentication.AttachOperatorToken(client, deadlocking.Services);
            response = await client.DeleteAsync($"/knowledge-bases/{knowledgeBase.Id}");
        }

        Assert.Equal(2, interceptor.Fired);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("1", response.Headers.RetryAfter?.ToString());
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/knowledge-bases/{knowledgeBase.Id}")).StatusCode);
        Assert.Equal(before, await FootprintAsync(knowledgeBase.Id));
    }

    /// <summary>
    /// Comandos (categoria de comando do EF, Information) e transações (categoria de
    /// transação, Debug) num log só, na ordem em que o EF os registrou. O
    /// <see cref="EmittedSqlCapture"/> da fixture só vê comandos, e é a transação que
    /// distingue "travou e apagou junto" de "travou e soltou".
    /// </summary>
    private sealed class CommandAndTransactionLog : ILoggerProvider
    {
        public const string TransactionCategory = "Microsoft.EntityFrameworkCore.Database.Transaction";

        private readonly ConcurrentQueue<string> _entries = new();

        public List<string> Entries => [.. _entries];

        public void Clear() => _entries.Clear();

        public ILogger CreateLogger(string categoryName) =>
            categoryName is EmittedSqlCapture.EfCommandCategory or TransactionCategory
                ? new Sink(_entries)
                : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose()
        {
        }

        private sealed class Sink(ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(formatter(state, exception));
        }
    }
}
