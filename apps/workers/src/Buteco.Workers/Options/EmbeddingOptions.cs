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

    /// <summary>
    /// Quantos fragmentos vão em <b>cada chamada</b> ao gerador de embedding.
    ///
    /// <para>
    /// <b>250 porque 267 passou e 442 falhou, com margem; não há teto medido.</b>
    /// Medido no piloto de 20/09/2026, contra o gateway de embedding do
    /// <c>.env.prod</c>, com o modelo de 4.096 dimensões: o documento
    /// <c>02 HISTORICO E STATUS</c> (~442 fragmentos) falhou com
    /// <c>502 upstream_error</c> nas três tentativas, e as duas metades dele
    /// (267 e 175 fragmentos) indexaram. Este número é a única coisa que a
    /// medição sustenta.
    /// </para>
    ///
    /// <para>
    /// <b>NÃO é "o limite seguro do gateway".</b> O formato do teto — por número
    /// de entradas, por bytes do corpo, ou por tempo de resposta do upstream —
    /// não foi estabelecido, e nenhuma das três hipóteses foi descartada.
    /// Chamar 250 de limite seria afirmar um mecanismo que ninguém mediu.
    /// </para>
    ///
    /// <para>
    /// <b>É configurável por isso</b>, e não por simetria com as outras
    /// propriedades: o valor é provisório por construção, e variá-lo é como o
    /// teto vai ser descoberto. Diferente dos parâmetros de fragmentação
    /// (<c>KnowledgeChunker</c>), que saíram de uma medição e ninguém precisa
    /// ajustar em produção — a convenção 2 separa os dois casos pelo cenário
    /// real de alguém precisar de outro valor, e aqui ele existe.
    /// </para>
    ///
    /// <para>
    /// <b>Gatilho de recalibração</b> (convenção 22): qualquer medição contra o
    /// gateway que estabeleça o formato do teto, ou a primeira falha de
    /// indexação com o lote em vigor. Quem remedir escreve o regime ao lado do
    /// número novo.
    /// </para>
    ///
    /// <para>
    /// Valor menor ou igual a zero <b>reprova o boot</b> — ver
    /// <c>EmbeddingBatchSizeValidation</c>. Não é corrigido em silêncio.
    /// </para>
    /// </summary>
    public int BatchSize { get; set; } = 250;
}
