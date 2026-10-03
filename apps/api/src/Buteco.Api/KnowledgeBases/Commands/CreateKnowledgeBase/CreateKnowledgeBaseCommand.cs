using Mediator;

namespace Buteco.Api.KnowledgeBases.Commands.CreateKnowledgeBase;

/// <summary>
/// Sem <c>Provider</c>, cria base <c>Manual</c>. Com <c>Provider</c> e <c>FolderId</c>,
/// cria base <c>Synced</c> com a pasta validada pelo <c>apps/connectors</c> (design.md
/// da change criacao-base-sincronizada). A forma já foi conferida no endpoint.
/// </summary>
public sealed record CreateKnowledgeBaseCommand(
    string Name,
    string Description,
    string? Provider = null,
    string? FolderId = null) : ICommand<CreateKnowledgeBaseResult>;
