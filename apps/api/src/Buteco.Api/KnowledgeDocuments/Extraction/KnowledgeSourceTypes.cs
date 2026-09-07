namespace Buteco.Api.KnowledgeDocuments.Extraction;

/// <summary>
/// Lista declarada de <c>SourceType</c> suportados. É um dos dois lados da
/// checagem de integridade do startup — o outro é o conjunto de chaves
/// efetivamente registradas via DI keyed (convenção 8, bidirecional).
///
/// <c>SourceType</c> é string aberta validada em runtime, nunca enum fechado,
/// pelo mesmo motivo de <c>Channel.ChannelType</c>: o conjunto cresce por
/// registro de extrator, não por recompilação de um enum.
/// </summary>
public static class KnowledgeSourceTypes
{
    /// <summary>
    /// Cobre <c>.md</c>, <c>.markdown</c> e <c>.txt</c> — extensão de arquivo e
    /// <c>SourceType</c> são conceitos distintos (design.md, D12). Texto puro
    /// sem marcação é markdown válido, e por isso <c>.txt</c> não precisa de
    /// extrator próprio.
    /// </summary>
    public const string Markdown = "markdown";

    public static readonly IReadOnlyList<string> All = [Markdown];
}
