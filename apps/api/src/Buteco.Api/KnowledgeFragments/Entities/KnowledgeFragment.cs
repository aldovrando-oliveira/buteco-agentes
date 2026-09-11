using Pgvector;

namespace Buteco.Api.KnowledgeFragments.Entities;

/// <summary>
/// Fragmento indexado de um
/// <see cref="Buteco.Api.KnowledgeDocuments.Entities.KnowledgeDocument"/> —
/// o texto que a busca da etapa 4 vai devolver, mais o vetor que a encontra.
///
/// <para>
/// <b>Esta entidade vive em <c>apps/api</c>, que nunca escreve nela.</b> Quem
/// escreve é <c>apps/workers</c>, e o motivo de a tabela nascer aqui é de
/// deploy, não de domínio: o <c>migrator</c> do <c>docker-compose.prod.yml</c>
/// só empacota bundles de <c>apps/api</c> e <c>apps/inbox</c>, e
/// <c>apps/workers</c> nunca aplica migração a banco real (design.md, D10).
/// É contraintuitivo o bastante para alguém "consertar" — não conserte.
/// </para>
/// </summary>
public class KnowledgeFragment
{
    public Guid Id { get; private set; }

    public Guid KnowledgeDocumentId { get; private set; }

    /// <summary>
    /// Desnormalizado de propósito. A busca da etapa 4 filtra por base antes de
    /// ordenar por distância, e sem esta coluna toda consulta precisaria de
    /// <c>JOIN</c> com <c>knowledge_documents</c> só para alcançar o filtro.
    /// </summary>
    public Guid KnowledgeBaseId { get; private set; }

    /// <summary>
    /// Posição do fragmento dentro do documento, a partir de zero. Não é chave
    /// de nada: existe para que a leitura de um documento inteiro tenha ordem
    /// estável, e para que o operador consiga localizar o trecho.
    /// </summary>
    public int Ordinal { get; private set; }

    /// <summary>
    /// Texto emitido pelo fragmentador — já com o prefixo de caminho de
    /// cabeçalhos, que é o que foi embedado. Guardar o texto exato que gerou o
    /// vetor é o que permite reembedar sem refragmentar.
    /// </summary>
    public string Text { get; private set; } = null!;

    /// <summary>
    /// Vetor na dimensão nativa do modelo, sem truncagem no cliente
    /// (design.md, D1). A coluna indexável por HNSW é derivada desta por SQL
    /// quando o índice fizer falta — HNSW recusa mais de 2000 dimensões em
    /// <c>vector</c> e mais de 4000 em <c>halfvec</c>, então truncar não é
    /// otimização, é a única forma de existir índice para este modelo.
    /// </summary>
    public Vector Embedding { get; private set; } = null!;

    /// <summary>
    /// Proveniência do vetor. <b>As três colunas abaixo não são metadado
    /// decorativo:</b> são o lado do índice na checagem bidirecional do boot de
    /// <c>apps/workers</c> (design.md, D6), que compara o que está gravado
    /// contra o que a configuração declara. Sem elas a checagem não tem contra o
    /// que comparar, e a troca silenciosa de modelo — que corrompe o índice sem
    /// erro nenhum — deixa de ser detectável.
    /// </summary>
    public string EmbeddingProvider { get; private set; } = null!;

    /// <inheritdoc cref="EmbeddingProvider"/>
    public string EmbeddingModel { get; private set; } = null!;

    /// <inheritdoc cref="EmbeddingProvider"/>
    public int EmbeddingDimensions { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private KnowledgeFragment()
    {
    }

    public KnowledgeFragment(
        Guid knowledgeDocumentId,
        Guid knowledgeBaseId,
        int ordinal,
        string text,
        Vector embedding,
        string embeddingProvider,
        string embeddingModel,
        int embeddingDimensions)
    {
        Id = Guid.NewGuid();
        KnowledgeDocumentId = knowledgeDocumentId;
        KnowledgeBaseId = knowledgeBaseId;
        Ordinal = ordinal;
        Text = text;
        Embedding = embedding;
        EmbeddingProvider = embeddingProvider;
        EmbeddingModel = embeddingModel;
        EmbeddingDimensions = embeddingDimensions;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
