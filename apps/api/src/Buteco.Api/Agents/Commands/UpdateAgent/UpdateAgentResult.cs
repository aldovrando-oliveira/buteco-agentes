using Buteco.Api.Agents.Responses;
using Buteco.Api.Providers;

namespace Buteco.Api.Agents.Commands.UpdateAgent;

/// <summary>
/// Sinal neutro devolvido pelo handler — sem nenhuma referência a
/// <c>Microsoft.AspNetCore.Http.HttpResults</c> (Decision 4 do design.md da
/// change backend-multi-provedor-llm). Duas causas de falha possíveis, que o
/// endpoint precisa distinguir para responder 404 vs. 400: <see cref="Found"/>
/// == false (id inexistente) ou <see cref="Validation"/> != Valid (provider/
/// model indisponível, agente existe mas não foi alterado).
/// </summary>
public sealed record UpdateAgentResult(AgentResponse? Agent, bool Found, ProviderValidationOutcome Validation)
{
    public static UpdateAgentResult NotFound() => new(null, Found: false, ProviderValidationOutcome.Valid);

    public static UpdateAgentResult ValidationFailed(ProviderValidationOutcome validation) => new(null, Found: true, validation);

    public static UpdateAgentResult Success(AgentResponse agent) => new(agent, Found: true, ProviderValidationOutcome.Valid);
}
