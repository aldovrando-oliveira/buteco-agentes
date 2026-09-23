namespace Buteco.Workers.EmbeddingMetrics;

/// <summary>
/// Vocabulário fechado gravado nas tabelas de métrica de embedding — sempre como
/// texto, nunca como ordinal (convenção 12): quem lê o banco na etapa de
/// agregação lê <c>'EmbeddingGateway'</c>, não <c>2</c>, e acrescentar um valor
/// não renumera os outros. Mesmo idioma de <c>ExecutionMetricsValues</c>.
/// </summary>
public static class EmbeddingMetricsValues
{
    /// <summary>
    /// Os dois consumidores do mesmo gateway. A separação é decisão de produto
    /// (design.md, D2): indexação é custo de cadastro e busca é custo por
    /// conversa, e um total único apagaria a distinção.
    /// </summary>
    public static class Purpose
    {
        public const string Indexing = "Indexing";
        public const string Search = "Search";
    }

    /// <summary>
    /// Como terminou a tentativa. Os mesmos quatro valores de
    /// <c>KnowledgeIndexingOutcome</c>, gravados como texto — o enum é o contrato
    /// entre serviço e consumidor, esta constante é o contrato com o banco, e
    /// misturar os dois é o que a convenção 12 evita.
    /// </summary>
    public static class Outcome
    {
        public const string Indexed = "Indexed";
        public const string RetryScheduled = "RetryScheduled";
        public const string Failed = "Failed";
        public const string Discarded = "Discarded";
    }

    /// <summary>
    /// A fase em que a indexação estava quando parou sem indexar (design.md, D5).
    /// </summary>
    /// <remarks>
    /// <b>Fase, e não o texto de operador.</b> A classificação pronta do
    /// repositório (<c>KnowledgeIndexingFailure.Describe</c>) produz texto de
    /// <b>tela</b>, e no único provedor de embedding implementado os braços de
    /// <c>429</c>, <c>401</c> e <c>403</c> são <b>inalcançáveis</b>: a exceção do
    /// caminho <c>openai</c> é <c>ClientResultException</c>, que deriva de
    /// <c>Exception</c> e não de <c>HttpRequestException</c> (verificado por
    /// execução). Agrupar M30 por aquele texto concluiria que o gateway nunca
    /// devolve 429 e nunca recusa credencial. A fase é determinada por onde o
    /// código estava, que é estável por construção; o status HTTP fica na linha
    /// filha de <c>embedding_calls</c>, onde <c>HttpStatusOf</c> o coloca
    /// corretamente.
    /// </remarks>
    public static class FailurePhase
    {
        /// <summary>A fragmentação não produziu trecho indexável, ou estourou.</summary>
        public const string Chunking = "Chunking";

        /// <summary>O gerador de embedding não pôde ser construído — provedor não configurado ou não suportado.</summary>
        public const string ProviderResolution = "ProviderResolution";

        /// <summary>A chamada ao gateway lançou. É onde o <c>502</c> mora.</summary>
        public const string EmbeddingGateway = "EmbeddingGateway";

        /// <summary>O lote devolveu número de vetores diferente do número de entradas.</summary>
        public const string VectorCountMismatch = "VectorCountMismatch";

        /// <summary>O provedor devolveu vetor com dimensão diferente da declarada.</summary>
        public const string DimensionMismatch = "DimensionMismatch";

        /// <summary>A gravação dos fragmentos ou do estado do documento falhou.</summary>
        public const string Persistence = "Persistence";
    }
}
