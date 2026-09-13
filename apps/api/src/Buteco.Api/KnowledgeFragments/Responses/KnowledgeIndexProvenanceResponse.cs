namespace Buteco.Api.KnowledgeFragments.Responses;

/// <summary>
/// Uma combinação de provedor, modelo e dimensão de embedding presente no índice,
/// com o número de fragmentos gravados com ela. Servida por
/// <c>GET /knowledge-index/diagnostics</c>.
///
/// <para>
/// <b>Proveniência é propriedade do SISTEMA, não da base</b> (design.md, D1), e
/// é por isso que não há <c>KnowledgeBaseId</c> aqui. Três evidências
/// independentes: a dimensão é fixada pelo tipo da coluna (<c>vector(4096)</c>),
/// que recusa vetor de outra dimensão na hora da gravação
/// (<c>expected 4096 dimensions, not 1536</c>, medido); a checagem de boot de
/// <c>apps/workers</c> exige combinação única no índice <b>inteiro</b>, sem
/// filtro de base; e o protótipo modela a proveniência como um objeto no estado
/// raiz. Uma resposta por base afirmaria que bases diferentes podem divergir, o
/// que o schema torna impossível.
/// </para>
///
/// <para>
/// <b>A lista PODE ter mais de um item, e isso NÃO é erro desta rota.</b> É o
/// estado em que <c>apps/workers</c> se recusa a subir — vetores de modelos
/// diferentes são incomparáveis — enquanto <c>apps/api</c> continua de pé. É
/// exatamente nesse estado que o operador abre a tela, e é a razão pela qual a
/// resposta é uma lista e não três campos escalares: com escalares, o estado que
/// motiva a visita não tem como ser representado.
/// </para>
///
/// <para>
/// <b><c>FragmentCount</c> é <c>long</c>, não <c>int</c>:</b> <c>count(*)</c> do
/// PostgreSQL é <c>bigint</c>, e o cast para <c>int</c> seria uma conversão sem
/// motivo num caminho que só cresce. Ele existe pelo caso anormal — saber que são
/// 500 fragmentos contra 149.500 decide se a saída é reindexar alguns documentos
/// ou o acervo inteiro — e sai de graça, porque <c>GROUP BY</c> com contagem custa
/// o mesmo que <c>DISTINCT</c> puro (medido).
/// </para>
///
/// <para>
/// <b>Nada aqui vem de configuração.</b> <c>apps/api</c> não tem a seção
/// <c>Embedding</c> — ela existe em um único <c>appsettings</c>, o de
/// <c>apps/workers</c> —, e índice vazio devolve lista vazia em vez de preencher
/// os campos com o valor declarado (design.md, D2).
/// </para>
/// </summary>
public sealed record KnowledgeIndexProvenanceResponse(
    string Provider,
    string Model,
    int Dimensions,
    long FragmentCount);
