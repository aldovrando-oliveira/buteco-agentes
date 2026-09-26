namespace Buteco.Api.RejectionMetrics.Entities;

/// <summary>
/// Uma task recusada por <c>apps/api</c> <b>antes</b> de qualquer publicação de
/// job — a fonte da métrica M29 (design.md da change <c>recusa-motivo-coleta</c>,
/// D2).
///
/// <para>
/// <b>Tabela própria, e não coluna em <c>a2a_tasks</c> nem linha em
/// <c>task_executions</c>.</b> O precedente que fechou a decisão está em
/// <c>AppDbContext.OnModelCreating</c>, na nota de <c>embedding_calls</c>:
/// reusar uma tabela existente obrigaria TODA consulta dela a filtrar, e quem
/// esquecesse o filtro receberia um número maior e <b>plausível</b>, não um erro.
/// Medido: 22 consultas por rota leem <c>task_executions</c> filtrando só pela
/// janela, e a recusa entraria em M1, M2, M6/M7, M21, M22, M24, M25, M26 e M32.
/// </para>
///
/// <para>
/// <b>Diferente das cinco tabelas de métrica, esta é escrita por
/// <c>apps/api</c></b> — é ele quem recusa na entrada. Por isso ela NÃO é
/// espelhada em <c>apps/workers</c>: ninguém lá a escreve nem a lê.
/// </para>
///
/// <para>
/// <b>Sem chave estrangeira</b>, e não por descuido. Para <c>a2a_tasks</c>:
/// quem grava aquela linha é o <c>ITaskStore</c> do SDK, por um caminho cuja
/// ordem contra esta escrita não é garantida — a FK transformaria uma corrida em
/// falha. Para <c>agents</c>: <c>a2a_tasks</c> tem FK com cascade, então o dia em
/// que houver rota de exclusão de agente a história de recusas desapareceria
/// junto — e a regra desta base é que a métrica registra o que aconteceu, e isso
/// não muda porque o catálogo mudou depois.
/// </para>
///
/// <para>
/// <b>Sem provedor e sem modelo</b>, de propósito: DUAS das quatro causas são
/// justamente a ausência de provedor ou modelo utilizáveis. Uma coluna preenchida
/// em parte dos casos seria lida como distribuição por qualquer agregação.
/// </para>
/// </summary>
public class TaskRejection
{
    public string TaskId { get; private set; } = null!;

    public Guid AgentId { get; private set; }

    /// <summary>
    /// Valor de <see cref="RejectionMetricsValues.Reason"/>. Obrigatório: a linha
    /// só nasce num dos sítios que decidem a recusa, então não existe recusa
    /// gravada sem motivo — e é isso que faz a soma dos motivos fechar com a
    /// contagem de recusas da mesma janela.
    /// </summary>
    public string Reason { get; private set; } = null!;

    /// <summary>
    /// O instante da recusa pelo <c>TimeProvider</c> de <c>apps/api</c>, nunca por
    /// <c>now()</c> no banco: janela cujo "agora" nasce no banco não é verificável
    /// de forma determinística. Não é o mesmo relógio de
    /// <c>a2a_tasks.status_timestamp</c>, e a diferença de microssegundos é
    /// trade-off aceito, igual ao de <c>task_executions.StartedAt</c>.
    /// </summary>
    public DateTimeOffset RejectedAt { get; private set; }

    private TaskRejection()
    {
    }

    public TaskRejection(string taskId, Guid agentId, string reason, DateTimeOffset rejectedAt)
    {
        TaskId = taskId;
        AgentId = agentId;
        Reason = reason;
        RejectedAt = rejectedAt;
    }
}
