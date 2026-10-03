using Buteco.Api.KnowledgeBases.Entities;

namespace Buteco.Api.KnowledgeBases.Responses;

/// <summary>
/// De onde vêm os documentos de uma base sincronizada (design.md da change
/// catalogo-base-sincronizada, D13). <c>provider</c> é string aberta. Nome e URL
/// são o snapshot da última sincronização concluída.
/// </summary>
public sealed record KnowledgeBaseSyncSourceResponse(string Provider, string FolderId, string FolderName, string FolderUrl)
{
    /// <summary>Nulo em base manual: "sei que não existe" (convenção 13).</summary>
    public static KnowledgeBaseSyncSourceResponse? FromEntity(KnowledgeBase knowledgeBase) =>
        knowledgeBase.ContentMode == KnowledgeBaseContentMode.Synced
            ? new(knowledgeBase.SyncProvider!, knowledgeBase.SyncFolderId!, knowledgeBase.SyncFolderName!, knowledgeBase.SyncFolderUrl!)
            : null;
}
