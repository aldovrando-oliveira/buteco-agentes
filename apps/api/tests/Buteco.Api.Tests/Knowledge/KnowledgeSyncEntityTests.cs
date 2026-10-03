using Buteco.Api.KnowledgeBases.Entities;
using Buteco.Api.KnowledgeDocuments.Entities;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// As regras do estado da sincronização e da revisão externa, na entidade, sem
/// banco nem host (design.md da change catalogo-base-sincronizada, D2 e D3).
/// </summary>
public class KnowledgeSyncEntityTests
{
    private static readonly DateTimeOffset T1 = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = T1.AddMinutes(5);
    private static readonly DateTimeOffset T3 = T1.AddMinutes(10);

    private static readonly KnowledgeBaseSyncIgnoredFile Spreadsheet = new("ref-1", "planilha.xlsx", "unsupported-type", null);

    private static KnowledgeBase NewSyncedBase() =>
        KnowledgeBase.CreateSynced("Base", "Descrição.", "google-drive", "pasta-1", "Atendimento", "https://drive/pasta-1");

    [Fact]
    public void RecordSyncFailure_PreservesLastCompletedIgnoredFilesAndFolderSnapshot()
    {
        var knowledgeBase = NewSyncedBase();
        knowledgeBase.RecordSyncSuccess(T1, "Atendimento", "https://drive/pasta-1", [Spreadsheet]);

        knowledgeBase.RecordSyncFailure(T2, "access-denied", "leitor@projeto");

        Assert.Equal(T1, knowledgeBase.LastSyncCompletedAt);
        Assert.Equal([Spreadsheet], knowledgeBase.SyncIgnoredFiles);
        Assert.Equal("Atendimento", knowledgeBase.SyncFolderName);
        Assert.Equal("https://drive/pasta-1", knowledgeBase.SyncFolderUrl);
        Assert.Equal(T2, knowledgeBase.LastSyncFinishedAt);
        Assert.Equal("access-denied", knowledgeBase.LastSyncErrorCode);
        Assert.Equal("leitor@projeto", knowledgeBase.LastSyncErrorDetail);
        Assert.Equal(T2, knowledgeBase.SyncFailingSince);
    }

    [Fact]
    public void RecordSyncFailure_Twice_KeepsTheFirstFailingSince()
    {
        var knowledgeBase = NewSyncedBase();

        knowledgeBase.RecordSyncFailure(T1, "access-denied", null);
        knowledgeBase.RecordSyncFailure(T2, "api-not-configured", null);

        Assert.Equal(T1, knowledgeBase.SyncFailingSince);
        Assert.Equal(T2, knowledgeBase.LastSyncFinishedAt);
        Assert.Equal("api-not-configured", knowledgeBase.LastSyncErrorCode);
        Assert.Null(knowledgeBase.LastSyncCompletedAt);
    }

    [Fact]
    public void RecordSyncSuccess_ClearsTheFailureAndReplacesTheSnapshot()
    {
        var knowledgeBase = NewSyncedBase();
        knowledgeBase.RecordSyncFailure(T1, "access-denied", "leitor@projeto");

        knowledgeBase.RecordSyncSuccess(T3, "Atendimento 2026", "https://drive/pasta-1?nova", []);

        Assert.Null(knowledgeBase.LastSyncErrorCode);
        Assert.Null(knowledgeBase.LastSyncErrorDetail);
        Assert.Null(knowledgeBase.SyncFailingSince);
        Assert.Equal(T3, knowledgeBase.LastSyncCompletedAt);
        Assert.Equal(T3, knowledgeBase.LastSyncFinishedAt);
        Assert.Equal("Atendimento 2026", knowledgeBase.SyncFolderName);
        Assert.Empty(knowledgeBase.SyncIgnoredFiles!);
        Assert.Equal("google-drive", knowledgeBase.SyncProvider);
        Assert.Equal("pasta-1", knowledgeBase.SyncFolderId);
    }

    [Fact]
    public void SyncedBase_BeforeAnySuccess_HasNullIgnoredFiles()
    {
        var knowledgeBase = NewSyncedBase();

        knowledgeBase.RecordSyncFailure(T1, "access-denied", null);

        Assert.Null(knowledgeBase.SyncIgnoredFiles);
    }

    [Fact]
    public void RecordingSyncState_OnManualBase_Throws()
    {
        var knowledgeBase = new KnowledgeBase("Manual", "Descrição.");

        Assert.Throws<InvalidOperationException>(() => knowledgeBase.RecordSyncFailure(T1, "access-denied", null));
        Assert.Throws<InvalidOperationException>(() => knowledgeBase.RecordSyncSuccess(T1, "x", "y", []));
    }

    [Fact]
    public void ApplyExternalRevision_WithSameTitleTypeAndText_ChangesOnlyTheExternalVersion()
    {
        var document = KnowledgeDocument.CreateSynced(Guid.NewGuid(), "Doc", "markdown", "# Texto\n", "ref-1", "v1");
        var updatedAt = document.UpdatedAt;
        var revision = document.ContentRevision;
        var status = document.IndexingStatus;
        var hash = document.ContentHash;

        var outcome = document.ApplyExternalRevision("Doc", "markdown", "# Texto\n", "v2");

        Assert.Equal(new KnowledgeDocumentUpdateOutcome(NeedsIndexing: false, ContentChanged: false, TitleChanged: false), outcome);
        Assert.Equal("v2", document.ExternalVersion);
        Assert.Equal(updatedAt, document.UpdatedAt);
        Assert.Equal(revision, document.ContentRevision);
        Assert.Equal(status, document.IndexingStatus);
        Assert.Equal(hash, document.ContentHash);
    }

    [Fact]
    public void ApplyExternalRevision_WithNewText_DelegatesToUpdate()
    {
        var document = KnowledgeDocument.CreateSynced(Guid.NewGuid(), "Doc", "markdown", "# Texto\n", "ref-1", "v1");

        var outcome = document.ApplyExternalRevision("Doc", "markdown", "# Outro\n", "v2");

        Assert.Equal(new KnowledgeDocumentUpdateOutcome(NeedsIndexing: true, ContentChanged: true, TitleChanged: false), outcome);
        Assert.Equal(2, document.ContentRevision);
        Assert.Equal("v2", document.ExternalVersion);
    }

    [Fact]
    public void ManualDocument_HasNoExternalReference()
    {
        var document = new KnowledgeDocument(Guid.NewGuid(), "Doc", "markdown", "# Texto\n");

        Assert.Equal(KnowledgeBaseContentMode.Manual, document.KnowledgeBaseContentMode);
        Assert.Null(document.ExternalRef);
        Assert.Null(document.ExternalVersion);
    }
}
