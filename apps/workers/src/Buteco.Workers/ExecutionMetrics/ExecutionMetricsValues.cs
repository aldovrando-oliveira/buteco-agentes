namespace Buteco.Workers.ExecutionMetrics;

/// <summary>
/// Vocabulário fechado gravado nas tabelas de métrica — sempre como texto, nunca
/// como ordinal (convenção 12): quem lê o banco na etapa de agregação lê
/// <c>'ContextLock'</c>, não <c>2</c>, e acrescentar um valor não renumera os
/// outros.
/// </summary>
public static class ExecutionMetricsValues
{
    public static class Origin
    {
        public const string External = "External";
        public const string Delegation = "Delegation";
    }

    public static class Purpose
    {
        public const string Turn = "Turn";
        public const string Compaction = "Compaction";
    }

    /// <summary>
    /// A fase em que a execução estava quando terminou sem sucesso (D12).
    /// </summary>
    /// <remarks>
    /// <b>Fase, e não texto de exceção.</b> A classificação pronta do
    /// repositório (<c>KnowledgeIndexingFailure.Describe</c>) produz texto de
    /// operador e distingue os quatro <c>throw</c> de <c>ChatClientResolver</c>
    /// por <c>Message.Contains</c> — o texto de uma exceção é de quem a lança, e
    /// muda sem aviso. A fase é determinada por onde o código estava, que é
    /// estável por construção. O detalhe da chamada que falhou fica na linha
    /// filha (<c>Failed</c>, <c>HttpStatus</c>).
    /// </remarks>
    public static class FailurePhase
    {
        public const string DelegationDepthExceeded = "DelegationDepthExceeded";
        public const string ContextLock = "ContextLock";
        public const string ChatClientResolution = "ChatClientResolution";
        public const string ToolResolution = "ToolResolution";
        public const string SessionLoad = "SessionLoad";
        public const string AgentRun = "AgentRun";
        public const string Persistence = "Persistence";
    }

    public static class DelegationOutcome
    {
        public const string Completed = "Completed";
        public const string TargetUnsuccessful = "TargetUnsuccessful";
        public const string Expired = "Expired";
        public const string NotStarted = "NotStarted";
    }
}
