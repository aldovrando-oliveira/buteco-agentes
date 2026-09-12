namespace Buteco.Workers.Knowledge.Execution;

/// <summary>
/// Um trecho recuperado, como o modelo o recebe. **Três campos, e a escolha dos
/// três é decisão medida** (design.md, D2 e V9).
/// </summary>
/// <param name="Documento">
/// O <c>Title</c> do documento **no catálogo** — o nome que o operador digitou e
/// vê no painel.
///
/// <para>
/// <b>Não é redundante com o que está dentro de <paramref name="Trecho"/>.</b> O
/// fragmentador injeta no próprio texto vetorizado um prefixo
/// <c>"Título &gt; Seção &gt; Subseção"</c>, mas aquele título vem do <c>#</c> do
/// markdown, não do catálogo — e medido no índice real os dois **diferem**: o
/// documento cujo <c>Title</c> é <c>"02 HISTORICO E STATUS"</c> emite fragmentos
/// prefixados por <c>"Buteco Agentes — Histórico e Status"</c>. Este é o único
/// nome que o operador controla, e o único que não está no trecho.
/// </para>
/// </param>
/// <param name="Trecho">
/// O texto do fragmento exatamente como gravado no índice — **é o texto que foi
/// vetorizado**, prefixo de caminho de cabeçalhos incluído. Por isso não existe
/// campo <c>headingPath</c> aqui: ele já está dentro deste, e um campo separado
/// duplicaria texto em toda chamada e divergiria no dia em que o prefixo mudasse
/// (<c>ChunkedFragment.HeadingPath</c> documenta que não é persistido por esse
/// mesmo motivo).
/// </param>
/// <param name="Distancia">
/// Distância de cosseno entre o vetor da consulta e o vetor deste fragmento.
/// Menor é mais próximo.
///
/// <para>
/// <b>Exposta sempre, em todos os resultados, sem filtragem prévia por valor</b>
/// — é a decisão da etapa <c>0c</c>, tomada contra o limiar e com evidência: com
/// 20 negativas e 83 positivas, as médias separam (0,3357 com alvo contra 0,4742
/// sem) mas as caudas se sobrepõem, e 16 das 83 positivas têm topo mais longe
/// que a negativa mais próxima. Nenhum corte serve, então o corte não existe e
/// quem decide é o agente.
/// </para>
///
/// <para>
/// Arredondada a quatro casas: a quinta não muda decisão nenhuma do modelo e
/// custa caractere em toda chamada de tool.
/// </para>
/// </param>
public sealed record KnowledgeSearchResult(string Documento, string Trecho, double Distancia);

/// <summary>
/// O que a tool de conhecimento devolve ao modelo.
/// </summary>
/// <param name="Trechos">
/// Os até <c>k</c> fragmentos mais próximos, em ordem crescente de distância.
/// Vazio **apenas** quando a base não tem nenhum fragmento indexado ou quando a
/// busca falhou — nunca como "não achei nada relevante", porque sem limiar um
/// índice povoado sempre devolve <c>min(k, n)</c>.
/// </param>
/// <param name="Aviso">
/// Nulo no caminho normal. Preenchido nos dois casos em que não houve o que
/// devolver, e o texto é que os distingue: base sem conteúdo indexado, ou falha
/// na consulta.
///
/// <para>
/// <b>O que este tipo deliberadamente NÃO tem</b> (design.md, D4, e convenção
/// 13): nenhum campo de relevância, acerto, confiança ou "isto responde à
/// pergunta". O sistema não sabe nada disso — ele sabe quais fragmentos estão
/// mais perto do vetor da consulta, e é só isso que ele afirma. Acrescentar um
/// campo desses é a regressão bem-intencionada que o guarda de asserção negativa
/// existe para reprovar.
/// </para>
/// </param>
public sealed record KnowledgeSearchToolResult(IReadOnlyList<KnowledgeSearchResult> Trechos, string? Aviso = null);
