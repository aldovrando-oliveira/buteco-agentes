namespace Buteco.Api.KnowledgeBases.Entities;

/// <summary>
/// Um arquivo da pasta que não entrou na base no último ciclo bem-sucedido
/// (design.md da change catalogo-base-sincronizada, D1).
/// </summary>
/// <remarks>
/// <see cref="Code"/> é código estável (<c>unsupported-type</c>,
/// <c>too-large</c>, <c>download-blocked</c>...), nunca frase: o texto exibido é
/// do frontend. <see cref="Detail"/> é opcional.
/// </remarks>
public sealed record KnowledgeBaseSyncIgnoredFile(string ExternalRef, string Name, string Code, string? Detail);
