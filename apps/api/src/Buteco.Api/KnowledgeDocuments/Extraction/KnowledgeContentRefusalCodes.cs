namespace Buteco.Api.KnowledgeDocuments.Extraction;

/// <summary>
/// Códigos das recusas de conteúdo (design.md da change codigo-recusa-conteudo-upsert,
/// D1). Saem na extensão <c>code</c> da resposta 400, nas rotas do operador e no upsert
/// de <c>/sync</c>, e a #105 os grava como motivo de arquivo ignorado. Têm a forma de
/// <see cref="KnowledgeSync.SyncCode"/>, e o texto exibido é do frontend.
/// </summary>
/// <remarks>
/// <c>unsupported-source-type</c>, e não <c>unsupported-type</c>: este é código do
/// apps/connectors para o tipo de ARQUIVO que o conector não converte; o daqui é o
/// <c>sourceType</c> do contrato, sem extrator. Os dois chegam à mesma lista de ignorados.
/// </remarks>
public static class KnowledgeContentRefusalCodes
{
    public const string TooLarge = "too-large";
    public const string UnsupportedSourceType = "unsupported-source-type";
    public const string NullCharacter = "null-character";
    public const string EmptyContent = "empty-content";
}
