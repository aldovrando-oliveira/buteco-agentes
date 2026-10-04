namespace Buteco.Api.Knowledge.Indexing;

/// <summary>
/// Os quatro números do despacho de pedidos de indexação (design.md da change
/// indexacao-sem-job-orfao, D3 e D8). Constantes no código, não configuração
/// (convenção 2): nenhum cenário real pede outro valor. É singleton trocável por
/// DI porque os testes encurtam a varredura e o limite, e isso não é motivo para
/// virar opção de ambiente.
/// </summary>
public sealed class KnowledgeIndexingRequestSchedule
{
    /// <summary>Intervalo da varredura que reenvia o que ficou para trás.</summary>
    public TimeSpan SweepInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Pedidos tomados por transação de despacho.</summary>
    public int BatchSize { get; init; } = 100;

    /// <summary>
    /// Limite do despacho no fim da escrita. Fica bem abaixo dos 30 s com que o
    /// <c>apps/connectors</c> chama o <c>apps/api</c> (<c>SyncApiClient.Timeout</c>).
    /// </summary>
    public TimeSpan RequestDispatchTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Por quanto tempo, depois de uma falha, as escritas pulam o despacho no fim da
    /// requisição (D8). É o intervalo da varredura, que é quem publica nesse tempo.
    /// </summary>
    public TimeSpan SkipWindow { get; init; } = TimeSpan.FromSeconds(30);
}
