namespace Buteco.Api.KnowledgeBases.Responses;

/// <summary>
/// Estado de indexação agregado de uma base, servido por
/// <c>GET /knowledge-bases/indexing-summary</c>.
///
/// <para>
/// <b>Recurso próprio, e não campos em <c>KnowledgeBaseResponse</c></b>
/// (design.md, D1). Aquele record tem <b>seis</b> sítios de construção, e
/// <b>quatro</b> são handlers de comando — criar, atualizar, ativar e desativar
/// uma base. Como ele é posicional, campo novo quebraria a compilação nos seis,
/// e os quatro de comando teriam de escolher entre fazer uma agregação que não
/// tem nada a ver com o que eles fazem, ou devolver zero — falso em atualizar,
/// ativar e desativar, e a convenção 13 o proíbe. Pior: nenhum teste pegaria o
/// zero, porque nenhum sítio de construção está em <c>tests/</c> e desserializar
/// campo zerado passa calado.
/// </para>
///
/// <para>
/// <b>O zero daqui é um zero MEDIDO, e é por isso que ele pode ser exibido.</b>
/// A agregação percorreu os documentos daquela base e não achou nenhum, então
/// <c>"Nenhum"</c> na tela é verdade. É o oposto de
/// <c>KnowledgeDocument.FragmentCount</c> igual a zero num documento nunca
/// indexado, que é o default de uma coluna que ninguém escreveu — e foi por isso
/// que a etapa 5a-1 o proibiu na tela (D5, "<c>0 fragmentos</c> é defeito do
/// protótipo"). Não confundir os dois zeros.
/// </para>
///
/// <para>
/// <b>Toda base do catálogo tem uma linha</b>, inclusive a sem documento nenhum
/// e a inativa. Omitir a base obrigaria o consumidor a interpretar ausência, que
/// é justamente o que produz afirmação sem base.
/// </para>
///
/// <para>
/// <b><c>Pending</c> e <c>Indexing</c> não têm contagem própria</b>, e não é
/// economia: o catálogo <b>não os distingue</b> (o protótipo pinta a coluna em
/// tom de aviso para pendente, indexando e falha igualmente). Quem os separa é o
/// detalhe da base, que já recebe o estado <b>por documento</b> na listagem de
/// documentos desde a etapa 2a. Um quarto campo seria campo sem consumidor
/// (convenção 2), e o complemento continua exato por subtração.
/// </para>
/// </summary>
public sealed record KnowledgeBaseIndexingSummaryResponse(
    Guid KnowledgeBaseId,
    int DocumentCount,
    int IndexedCount,
    int FailedCount);
