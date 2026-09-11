namespace Buteco.Workers.Options;

/// <summary>
/// Modelo de embedding usado pela indexação de bases de conhecimento.
///
/// <para>
/// <b>Não há credencial aqui, e isso é decisão.</b> A chave e o endpoint vêm de
/// <see cref="ChatClientOptions"/> (seção <c>OpenAI</c>), reusados: aquele
/// endpoint já é no formato OpenAI, e o gateway que serve o modelo de embedding
/// é o mesmo que serve o de chat. Duplicar a credencial numa seção nova criaria
/// duas fontes para o mesmo segredo, que divergem no primeiro rodízio de chave.
/// </para>
/// </summary>
public sealed class EmbeddingOptions
{
    public const string SectionName = "Embedding";

    public string Provider { get; set; } = "openai";

    /// <summary>
    /// Escolhido por medição, não por disponibilidade (etapas <c>0b</c> e
    /// <c>0c</c>, em <c>02-HISTORICO_E_STATUS.md</c>): o modelo que o ambiente
    /// servia por padrão <b>empata com busca lexical</b> em recall@5 e perde em
    /// R@1 — com ele, a coluna vetorial não se justificaria.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Dimensão declarada, conferida contra a que o provedor devolve **no
    /// momento da gravação** (spec "Vetor gravado é o vetor cheio do modelo").
    ///
    /// A conferência não é paranoia: foi medido que o gateway **aceita** o
    /// parâmetro <c>dimensions</c> e o **ignora silenciosamente**. Sem conferir,
    /// um modelo trocado por outro de dimensão diferente gravaria vetores
    /// incompatíveis sem erro nenhum, e o índice ficaria corrompido em silêncio.
    /// </summary>
    public int Dimensions { get; set; }
}
