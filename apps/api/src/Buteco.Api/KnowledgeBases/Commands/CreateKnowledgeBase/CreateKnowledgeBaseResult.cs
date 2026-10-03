using Buteco.Api.KnowledgeBases.Responses;
using Buteco.Api.KnowledgeSync.Connectors;

namespace Buteco.Api.KnowledgeBases.Commands.CreateKnowledgeBase;

public enum CreateKnowledgeBaseOutcome
{
    Created,

    /// <summary>A pasta já é de outra base, ativa ou inativa (D5).</summary>
    FolderInUse,

    /// <summary>O <c>apps/connectors</c> não validou a pasta, ou não respondeu (D2 e D3).</summary>
    FolderValidationFailed,
}

/// <summary>Os três desfechos do cadastro (design.md da change criacao-base-sincronizada).</summary>
public sealed record CreateKnowledgeBaseResult(
    CreateKnowledgeBaseOutcome Outcome,
    KnowledgeBaseResponse? KnowledgeBase = null,
    Guid? ConflictingKnowledgeBaseId = null,
    string? ConflictingKnowledgeBaseName = null,
    ConnectorsFolderResult? ValidationFailure = null)
{
    public static CreateKnowledgeBaseResult Created(KnowledgeBaseResponse knowledgeBase) =>
        new(CreateKnowledgeBaseOutcome.Created, KnowledgeBase: knowledgeBase);

    public static CreateKnowledgeBaseResult FolderInUse(Guid id, string name) =>
        new(CreateKnowledgeBaseOutcome.FolderInUse, ConflictingKnowledgeBaseId: id, ConflictingKnowledgeBaseName: name);

    public static CreateKnowledgeBaseResult ValidationFailed(ConnectorsFolderResult failure) =>
        new(CreateKnowledgeBaseOutcome.FolderValidationFailed, ValidationFailure: failure);
}
