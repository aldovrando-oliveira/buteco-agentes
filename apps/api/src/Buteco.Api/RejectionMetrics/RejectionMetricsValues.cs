namespace Buteco.Api.RejectionMetrics;

/// <summary>
/// Vocabulário fechado gravado em <c>task_rejections</c> — sempre como texto,
/// nunca como ordinal (convenção 12): quem lê o banco na agregação lê
/// <c>'AgentInactive'</c>, não <c>1</c>, e acrescentar um valor não renumera os
/// outros.
/// </summary>
/// <remarks>
/// <para>
/// <b>O valor sai do SÍTIO DO CÓDIGO que decidiu a recusa, nunca de texto de
/// mensagem</b> (design.md, D3). Vale inclusive para a mensagem de razão que a
/// <c>TaskUpdater.RejectAsync</c> aceita e que hoje ninguém passa: ela é prosa
/// para o cliente, de quem a escreve, e muda sem aviso. A base já recusou duas
/// vezes classificar por texto — <c>KnowledgeIndexingFailure.Describe</c>, que
/// distingue quatro <c>throw</c> por <c>Message.Contains</c>, e o
/// <c>FailurePhase</c> de <c>task_executions</c>.
/// </para>
///
/// <para>
/// <b>São QUATRO valores, e o quarto é o achado da change</b> (design.md, D4): o
/// sítio de agente inativo carrega DUAS causas, porque
/// <c>EnqueueingAgentHandler.GetAgentStateAsync</c> projetava para um
/// <c>record struct</c> e o <c>FirstOrDefaultAsync</c> devolvia <c>default</c>
/// — com <c>IsActive = false</c> — quando o agente não existia. O <c>default</c>
/// é <b>ausência de leitura</b>, não inatividade, e gravar
/// <see cref="Reason.AgentInactive"/> sobre ele afirmaria um estado que ninguém
/// leu (convenção 13). A distinção não é cosmética para quem opera: um se resolve
/// reativando o agente, o outro significa que o cliente está chamando o endereço
/// A2A de um agente que não está lá.
/// </para>
///
/// <para>
/// <see cref="Reason.ProviderNotConfigured"/> reusa de propósito o nome que
/// <c>ProviderValidationOutcome</c> já dá ao mesmo fato no cadastro: mesmo
/// conceito, mesma palavra, um vocabulário só para quem lê os dois.
/// </para>
/// </remarks>
public static class RejectionMetricsValues
{
    public static class Reason
    {
        /// <summary>Nenhuma linha de agente com aquele id (`EnqueueingAgentHandler:27`).</summary>
        public const string AgentNotFound = "AgentNotFound";

        /// <summary>A linha existe e <c>IsActive = false</c> (`:27`).</summary>
        public const string AgentInactive = "AgentInactive";

        /// <summary><c>Provider</c> ou <c>Model</c> nulos no agente (`:36`).</summary>
        public const string ProviderOrModelMissing = "ProviderOrModelMissing";

        /// <summary>Provedor sem chave no ambiente de <c>apps/api</c> (`:44`).</summary>
        public const string ProviderNotConfigured = "ProviderNotConfigured";

        /// <summary>
        /// O vocabulário inteiro, para o guarda que afirma que nenhum outro valor
        /// aparece na coluna. Não é ponto de extensão: acrescentar valor aqui sem
        /// acrescentar o sítio que o grava deixa a lista mentindo.
        /// </summary>
        public static readonly IReadOnlyList<string> All =
        [
            AgentNotFound,
            AgentInactive,
            ProviderOrModelMissing,
            ProviderNotConfigured,
        ];
    }
}
