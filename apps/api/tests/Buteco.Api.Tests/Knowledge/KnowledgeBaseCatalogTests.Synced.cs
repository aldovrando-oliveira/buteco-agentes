using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Entities;
using Buteco.Api.KnowledgeBases.Requests;
using Buteco.Api.KnowledgeBases.Responses;
using Buteco.Api.KnowledgeSync.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// Tipo de conteúdo, origem e estado da sincronização da base (design.md da change
/// catalogo-base-sincronizada). <c>partial</c> da classe existente, e não classe
/// nova: <c>IClassFixture</c> é por classe, e uma classe nova subiria mais um
/// contêiner (D12).
/// </summary>
public partial class KnowledgeBaseCatalogTests
{
    private HttpClient Connectors => KnowledgeSyncTestSeed.CreateConnectorsClient(factory);

    /// <summary>
    /// SQL direto pela conexão do próprio <c>DbContext</c>, num escopo novo:
    /// <c>GetConnectionString()</c> devolve a string sem a senha, e uma conexão nova
    /// montada com ela não autentica.
    /// </summary>
    private async Task ExecuteSqlAsync(string sql)
    {
        using var scope = factory.Services.CreateScope();
        var connection = (NpgsqlConnection)scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string InsertSyncedBaseSql(Guid id, string folderId, bool isActive = true) => $"""
        INSERT INTO knowledge_bases
            ("Id", "Name", "Description", "IsActive", "CreatedAt", "UpdatedAt", "ContentMode",
             "SyncProvider", "SyncFolderId", "SyncFolderName", "SyncFolderUrl")
        VALUES ('{id}', 'Base direta', 'Descrição.', {(isActive ? "TRUE" : "FALSE")}, now(), now(), 'Synced',
                'google-drive', '{folderId}', 'Pasta', 'https://drive/{folderId}');
        """;

    // --- Cadastro com contentMode (D11) ----------------------------------------

    [Fact]
    public async Task CreateBase_WithoutContentMode_CreatesManualBaseWithNullSyncFields()
    {
        var created = await CreateBaseAsync("Base sem tipo");

        Assert.Equal(KnowledgeBaseContentMode.Manual, created.ContentMode);
        Assert.Null(created.SyncSource);
        Assert.Null(created.SyncState);
    }

    [Fact]
    public async Task CreateBase_WithManualContentMode_CreatesManualBase()
    {
        var response = await _client.PostAsJsonAsync(
            "/knowledge-bases", new CreateKnowledgeBaseRequest("Base manual explícita", "Descrição.", "Manual"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<KnowledgeBaseResponse>())!;
        Assert.Equal(KnowledgeBaseContentMode.Manual, created.ContentMode);
    }

    // "Synced" saiu deste caso com a change criacao-base-sincronizada (#104): a rota
    // passou a criar base sincronizada, e o cadastro está em
    // KnowledgeBaseCatalogTests.SyncedCreation.cs. Valor desconhecido continua 400.
    [Theory]
    [InlineData("Sincronizada")]
    [InlineData("synced")]
    public async Task CreateBase_WithUnknownContentMode_IsRefusedAndCreatesNothing(string contentMode)
    {
        var name = $"Base recusada {contentMode} {Guid.NewGuid():N}";

        var response = await _client.PostAsJsonAsync(
            "/knowledge-bases", new CreateKnowledgeBaseRequest(name, "Descrição.", contentMode));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("contentMode", out _));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await dbContext.KnowledgeBases.AnyAsync(knowledgeBase => knowledgeBase.Name == name));
    }

    // --- Imutabilidade (D11) --------------------------------------------------

    [Fact]
    public async Task UpdateSyncedBase_WithDifferentModeProviderAndFolder_KeepsTheStoredValues()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services, folderId: $"pasta-original-{Guid.NewGuid():N}");

        var response = await _client.PutAsJsonAsync($"/knowledge-bases/{knowledgeBase.Id}", new
        {
            name = "Nome novo",
            description = "Descrição nova.",
            contentMode = "Manual",
            provider = "dropbox",
            folderId = "outra-pasta",
            folderName = "Outra",
            folderUrl = "https://outra",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await KnowledgeSyncTestSeed.ReadBaseAsync(factory.Services, knowledgeBase.Id);
        Assert.Equal("Nome novo", stored.Name);
        Assert.Equal("Descrição nova.", stored.Description);
        Assert.Equal(KnowledgeBaseContentMode.Synced, stored.ContentMode);
        Assert.Equal(KnowledgeSyncTestSeed.Provider, stored.SyncProvider);
        Assert.Equal(knowledgeBase.SyncFolderId, stored.SyncFolderId);
        Assert.Equal(knowledgeBase.SyncFolderName, stored.SyncFolderName);
        Assert.Equal(knowledgeBase.SyncFolderUrl, stored.SyncFolderUrl);
    }

    [Fact]
    public async Task UpdateManualBase_WithSyncedContentMode_StaysManual()
    {
        var created = await CreateBaseAsync("Base manual imutável");

        var response = await _client.PutAsJsonAsync($"/knowledge-bases/{created.Id}", new
        {
            name = "Base manual imutável",
            description = "Descrição.",
            contentMode = "Synced",
            provider = "google-drive",
            folderId = "pasta-x",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await KnowledgeSyncTestSeed.ReadBaseAsync(factory.Services, created.Id);
        Assert.Equal(KnowledgeBaseContentMode.Manual, stored.ContentMode);
        Assert.Null(stored.SyncProvider);
        Assert.Null(stored.SyncFolderId);
    }

    [Fact]
    public async Task SyncedBase_EditActivateAndDeactivate_PreserveSourceAndState()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        (await Connectors.RecordFailureAsync(knowledgeBase.Id)).EnsureSuccessStatusCode();
        var before = await KnowledgeSyncTestSeed.ReadBaseAsync(factory.Services, knowledgeBase.Id);

        var edit = await _client.PutAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}", new UpdateKnowledgeBaseRequest("Editada", "Descrição editada."));
        var deactivate = await _client.PostAsync($"/knowledge-bases/{knowledgeBase.Id}/deactivate", null);
        var activate = await _client.PostAsync($"/knowledge-bases/{knowledgeBase.Id}/activate", null);

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        var after = await KnowledgeSyncTestSeed.ReadBaseAsync(factory.Services, knowledgeBase.Id);
        Assert.Equal(before.SyncFolderId, after.SyncFolderId);
        Assert.Equal(before.SyncFolderName, after.SyncFolderName);
        Assert.Equal(before.LastSyncErrorCode, after.LastSyncErrorCode);
        Assert.Equal(before.SyncFailingSince, after.SyncFailingSince);
        Assert.Equal(before.LastSyncFinishedAt, after.LastSyncFinishedAt);
    }

    // --- O banco garante as combinações (D2, D4, D5) --------------------------

    [Fact]
    public async Task Database_RefusesSyncedBaseWithoutFolder()
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteSqlAsync($"""
            INSERT INTO knowledge_bases ("Id", "Name", "Description", "IsActive", "CreatedAt", "UpdatedAt", "ContentMode", "SyncProvider")
            VALUES ('{Guid.NewGuid()}', 'Sem pasta', 'Descrição.', TRUE, now(), now(), 'Synced', 'google-drive');
            """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("CK_knowledge_bases_sync_source", exception.ConstraintName);
    }

    [Fact]
    public async Task Database_RefusesManualBaseWithFolder()
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteSqlAsync($"""
            INSERT INTO knowledge_bases ("Id", "Name", "Description", "IsActive", "CreatedAt", "UpdatedAt", "ContentMode", "SyncProvider", "SyncFolderId")
            VALUES ('{Guid.NewGuid()}', 'Manual com pasta', 'Descrição.', TRUE, now(), now(), 'Manual', 'google-drive', 'pasta');
            """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("CK_knowledge_bases_sync_source", exception.ConstraintName);
    }

    [Fact]
    public async Task Database_RefusesFailingSinceWithoutError()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteSqlAsync(
            $"UPDATE knowledge_bases SET \"SyncFailingSince\" = now() WHERE \"Id\" = '{knowledgeBase.Id}';"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("CK_knowledge_bases_sync_failing_since", exception.ConstraintName);
    }

    [Fact]
    public async Task FolderIndex_RefusesSecondBaseWithTheSameFolder()
    {
        var folderId = $"pasta-unica-{Guid.NewGuid():N}";
        await ExecuteSqlAsync(InsertSyncedBaseSql(Guid.NewGuid(), folderId));

        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteSqlAsync(InsertSyncedBaseSql(Guid.NewGuid(), folderId)));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("IX_knowledge_bases_SyncProvider_SyncFolderId", exception.ConstraintName);
    }

    [Fact]
    public async Task FolderIndex_InactiveBaseStillHoldsTheFolder()
    {
        var folderId = $"pasta-inativa-{Guid.NewGuid():N}";
        await ExecuteSqlAsync(InsertSyncedBaseSql(Guid.NewGuid(), folderId, isActive: false));

        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteSqlAsync(InsertSyncedBaseSql(Guid.NewGuid(), folderId)));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task FolderIndex_IdsThatDifferOnlyByCase_AreDifferentFolders()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await ExecuteSqlAsync(InsertSyncedBaseSql(Guid.NewGuid(), $"AbC-{suffix}"));

        var exception = await Record.ExceptionAsync(() => ExecuteSqlAsync(InsertSyncedBaseSql(Guid.NewGuid(), $"abc-{suffix}")));

        Assert.Null(exception);
    }

    /// <summary>
    /// Corrida no índice da pasta (D5): duas conexões, cada uma na sua transação,
    /// inserem a mesma pasta depois de uma barreira. A segunda fica bloqueada na
    /// entrada não confirmada do índice até a primeira terminar, e então recebe
    /// <c>23505</c>. Repetido em laço curto; a contagem de violações por iteração é
    /// a evidência de que a corrida aconteceu em todas, e não só a ausência de
    /// duplicata.
    /// </summary>
    [Fact]
    public async Task FolderIndex_ConcurrentInsertsOfTheSameFolder_ExactlyOneWins()
    {
        const int iterations = 10;
        var violations = 0;

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var folderId = $"pasta-corrida-{Guid.NewGuid():N}";
            using var barrier = new Barrier(2);

            async Task<bool> InsertAsync()
            {
                using var scope = factory.Services.CreateScope();
                var connection = (NpgsqlConnection)scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetDbConnection();
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                barrier.SignalAndWait();
                try
                {
                    await using var command = new NpgsqlCommand(InsertSyncedBaseSql(Guid.NewGuid(), folderId), connection, transaction);
                    await command.ExecuteNonQueryAsync();
                    await transaction.CommitAsync();
                    return true;
                }
                catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
                {
                    Interlocked.Increment(ref violations);
                    return false;
                }
            }

            var results = await Task.WhenAll(Task.Run(InsertAsync), Task.Run(InsertAsync));

            Assert.Equal(1, results.Count(won => won));
        }

        Assert.Equal(iterations, violations);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var duplicated = await dbContext.KnowledgeBases
            .Where(knowledgeBase => knowledgeBase.SyncFolderId!.StartsWith("pasta-corrida-"))
            .GroupBy(knowledgeBase => knowledgeBase.SyncFolderId)
            .CountAsync(group => group.Count() > 1);
        Assert.Equal(0, duplicated);
    }

    // --- Resultado de ciclo (D2) ----------------------------------------------

    private async Task<KnowledgeBaseResponse> ReadResponseAsync(Guid id) =>
        (await _client.GetFromJsonAsync<KnowledgeBaseResponse>($"/knowledge-bases/{id}"))!;

    [Fact]
    public async Task RecordingAFailure_PreservesTheLastCompletedSync()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        (await Connectors.RecordSuccessAsync(knowledgeBase.Id)).EnsureSuccessStatusCode();
        var completed = (await ReadResponseAsync(knowledgeBase.Id)).SyncState!.LastCompletedAt;

        var response = await Connectors.RecordFailureAsync(knowledgeBase.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = (await ReadResponseAsync(knowledgeBase.Id)).SyncState!;
        Assert.NotNull(completed);
        Assert.Equal(completed, state.LastCompletedAt);
        Assert.Equal("access-denied", state.LastError!.Code);
        Assert.NotNull(state.FailingSince);
    }

    [Fact]
    public async Task TwoFailures_KeepTheFirstFailingSinceAndAdvanceLastFinished()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        (await Connectors.RecordFailureAsync(knowledgeBase.Id)).EnsureSuccessStatusCode();
        var first = (await ReadResponseAsync(knowledgeBase.Id)).SyncState!;

        (await Connectors.RecordFailureAsync(knowledgeBase.Id, "api-not-configured", null)).EnsureSuccessStatusCode();
        var second = (await ReadResponseAsync(knowledgeBase.Id)).SyncState!;

        Assert.Equal(first.FailingSince, second.FailingSince);
        Assert.True(second.LastFinishedAt > first.LastFinishedAt);
        Assert.Equal("api-not-configured", second.LastError!.Code);
        Assert.Null(second.LastError.Detail);
    }

    [Fact]
    public async Task Success_ClearsTheFailureAndUpdatesFolderNameWithoutTouchingProviderOrFolder()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        (await Connectors.RecordFailureAsync(knowledgeBase.Id)).EnsureSuccessStatusCode();

        var response = await Connectors.RecordSuccessAsync(knowledgeBase.Id, "Atendimento renomeada");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var read = await ReadResponseAsync(knowledgeBase.Id);
        Assert.Null(read.SyncState!.FailingSince);
        Assert.Null(read.SyncState.LastError);
        Assert.NotNull(read.SyncState.LastCompletedAt);
        Assert.Equal("Atendimento renomeada", read.SyncSource!.FolderName);
        Assert.Equal(KnowledgeSyncTestSeed.Provider, read.SyncSource.Provider);
        Assert.Equal(knowledgeBase.SyncFolderId, read.SyncSource.FolderId);
    }

    [Fact]
    public async Task Failure_KeepsIgnoredFilesAndFolderName()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        (await Connectors.RecordSuccessAsync(
            knowledgeBase.Id,
            "Atendimento",
            new SyncIgnoredFileRequest("ref-1", "planilha.xlsx", "unsupported-type", null),
            new SyncIgnoredFileRequest("ref-2", "manual.pdf", "too-large", "3 MiB"))).EnsureSuccessStatusCode();

        (await Connectors.RecordFailureAsync(knowledgeBase.Id)).EnsureSuccessStatusCode();

        var read = await ReadResponseAsync(knowledgeBase.Id);
        Assert.Equal(["ref-1", "ref-2"], read.SyncState!.IgnoredFiles!.Select(file => file.ExternalRef));
        Assert.Equal("Atendimento", read.SyncSource!.FolderName);
    }

    public static TheoryData<object> InvalidSyncResults => new()
    {
        new { outcome = "Failed", error = new { code = "Sem acesso à pasta" } },
        new { outcome = "Failed", error = new { code = "access-denied" }, folderName = "Outra" },
        new { outcome = "Failed", error = new { code = "access-denied" }, ignoredFiles = Array.Empty<object>() },
        new { outcome = "Failed" },
        new { outcome = "Succeeded", folderName = "A", folderUrl = "u", ignoredFiles = Array.Empty<object>(), error = new { code = "access-denied" } },
        new { outcome = "Succeeded", folderName = "A", folderUrl = "u" },
        new
        {
            outcome = "Succeeded", folderName = "A", folderUrl = "u",
            ignoredFiles = new[]
            {
                new { externalRef = "ref-1", name = "a", code = "too-large" },
                new { externalRef = "ref-1", name = "b", code = "too-large" },
            },
        },
        new { outcome = "Succeeded", folderName = "A", folderUrl = "u", ignoredFiles = new[] { new { externalRef = "ref-1", name = "a", code = "Arquivo grande" } } },
        new { outcome = "Talvez" },
    };

    [Theory]
    [MemberData(nameof(InvalidSyncResults))]
    public async Task InvalidSyncResult_IsRefusedAndLeavesTheStateAlone(object body)
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);

        var response = await Connectors.PostAsJsonAsync(KnowledgeSyncTestSeed.SyncResultsPath(knowledgeBase.Id), body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var stored = await KnowledgeSyncTestSeed.ReadBaseAsync(factory.Services, knowledgeBase.Id);
        Assert.Null(stored.LastSyncFinishedAt);
        Assert.Null(stored.LastSyncErrorCode);
        Assert.Null(stored.SyncIgnoredFiles);
        Assert.Equal(knowledgeBase.SyncFolderName, stored.SyncFolderName);
    }

    [Fact]
    public async Task SyncResult_OnManualBase_IsConflictAndLeavesNoState()
    {
        var created = await CreateBaseAsync("Base manual sem ciclo");

        var response = await Connectors.RecordFailureAsync(created.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var read = await ReadResponseAsync(created.Id);
        Assert.Null(read.SyncState);
    }

    [Fact]
    public async Task SyncResult_OnMissingBase_IsNotFound()
    {
        var response = await Connectors.RecordFailureAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Failure_DoesNotTouchDocumentsNorHistory()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        (await Connectors.UpsertAsync(knowledgeBase.Id, "ref-doc-1")).EnsureSuccessStatusCode();
        (await Connectors.UpsertAsync(knowledgeBase.Id, "ref-doc-2")).EnsureSuccessStatusCode();
        var eventsBefore = await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, knowledgeBase.Id);

        (await Connectors.RecordFailureAsync(knowledgeBase.Id)).EnsureSuccessStatusCode();

        var refs = await Connectors.GetFromJsonAsync<List<JsonElement>>(KnowledgeSyncTestSeed.DocumentsPath(knowledgeBase.Id));
        Assert.Equal(2, refs!.Count);
        Assert.Equal(eventsBefore, await KnowledgeTestClient.CountDocumentEventsInDatabaseAsync(factory.Services, knowledgeBase.Id));
    }

    // --- Formato de fio (D13), sobre o texto da resposta HTTP real -----------

    [Fact]
    public async Task SyncedBaseResponse_UsesCamelCaseAndStringCodesOnTheWire()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);
        (await Connectors.RecordSuccessAsync(
            knowledgeBase.Id, "Atendimento", new SyncIgnoredFileRequest("ref-planilha", "planilha.xlsx", "unsupported-type", null))).EnsureSuccessStatusCode();
        (await Connectors.RecordFailureAsync(knowledgeBase.Id, "access-denied", "leitor@projeto.iam.gserviceaccount.com")).EnsureSuccessStatusCode();

        var json = await _client.GetStringAsync($"/knowledge-bases/{knowledgeBase.Id}");

        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        Assert.Equal("Synced", root.GetProperty("contentMode").GetString());
        var source = root.GetProperty("syncSource");
        Assert.Equal("google-drive", source.GetProperty("provider").GetString());
        Assert.Equal(knowledgeBase.SyncFolderId, source.GetProperty("folderId").GetString());
        Assert.Equal(JsonValueKind.String, source.GetProperty("folderName").ValueKind);
        Assert.Equal(JsonValueKind.String, source.GetProperty("folderUrl").ValueKind);
        var state = root.GetProperty("syncState");
        Assert.Equal(JsonValueKind.String, state.GetProperty("lastCompletedAt").ValueKind);
        Assert.Equal(JsonValueKind.String, state.GetProperty("lastFinishedAt").ValueKind);
        Assert.Equal(JsonValueKind.String, state.GetProperty("failingSince").ValueKind);
        Assert.Equal("access-denied", state.GetProperty("lastError").GetProperty("code").GetString());
        Assert.Equal("leitor@projeto.iam.gserviceaccount.com", state.GetProperty("lastError").GetProperty("detail").GetString());
        var ignored = state.GetProperty("ignoredFiles").EnumerateArray().Single();
        Assert.Equal("ref-planilha", ignored.GetProperty("externalRef").GetString());
        Assert.Equal("planilha.xlsx", ignored.GetProperty("name").GetString());
        Assert.Equal("unsupported-type", ignored.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, ignored.GetProperty("detail").ValueKind);

        // Asserção negativa: nenhuma chave em PascalCase, e o tipo nunca ordinal.
        foreach (var pascal in new[] { "ContentMode", "SyncSource", "SyncState", "LastCompletedAt", "FailingSince", "IgnoredFiles", "ExternalRef", "FolderId", "LastError" })
        {
            Assert.DoesNotContain($"\"{pascal}\"", json, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("\"contentMode\":1", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManualBaseResponse_HasNullSyncSourceAndState()
    {
        var created = await CreateBaseAsync("Base manual no fio");

        var json = await _client.GetStringAsync("/knowledge-bases");

        using var parsed = JsonDocument.Parse(json);
        var row = parsed.RootElement.EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == created.Id);
        Assert.Equal("Manual", row.GetProperty("contentMode").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("syncSource").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("syncState").ValueKind);
    }

    [Fact]
    public async Task NeverSyncedBase_HasNullInstantsAndNullIgnoredFiles()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services);

        var json = await _client.GetStringAsync($"/knowledge-bases/{knowledgeBase.Id}");

        using var parsed = JsonDocument.Parse(json);
        var state = parsed.RootElement.GetProperty("syncState");
        Assert.Equal(JsonValueKind.Null, state.GetProperty("lastCompletedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("lastFinishedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("failingSince").ValueKind);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("lastError").ValueKind);
        // Nulo, e não lista vazia: nenhuma listagem aconteceu (convenção 13).
        Assert.Equal(JsonValueKind.Null, state.GetProperty("ignoredFiles").ValueKind);
    }

    [Fact]
    public async Task InactiveFailingSyncedBase_IsListedWithFailingSince()
    {
        var knowledgeBase = await KnowledgeSyncTestSeed.SeedSyncedBaseAsync(factory.Services, isActive: false);
        (await Connectors.RecordFailureAsync(knowledgeBase.Id)).EnsureSuccessStatusCode();

        var listed = await _client.GetFromJsonAsync<List<KnowledgeBaseResponse>>("/knowledge-bases");

        var row = listed!.Single(item => item.Id == knowledgeBase.Id);
        Assert.False(row.IsActive);
        Assert.NotNull(row.SyncState!.FailingSince);
    }
}
