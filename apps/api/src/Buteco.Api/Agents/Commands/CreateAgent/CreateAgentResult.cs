using Buteco.Api.Agents.Responses;
using Buteco.Api.Providers;

namespace Buteco.Api.Agents.Commands.CreateAgent;

/// <summary>
/// Sinal neutro devolvido pelo handler — sem nenhuma referência a
/// <c>Microsoft.AspNetCore.Http.HttpResults</c> (Decision 4 do design.md da
/// change backend-multi-provedor-llm). <see cref="Agent"/> só é preenchido
/// quando <see cref="Validation"/> é <see cref="ProviderValidationOutcome.Valid"/>.
/// </summary>
public sealed record CreateAgentResult(AgentResponse? Agent, ProviderValidationOutcome Validation)
{
    public static CreateAgentResult Success(AgentResponse agent) => new(agent, ProviderValidationOutcome.Valid);

    public static CreateAgentResult Failed(ProviderValidationOutcome validation) => new(null, validation);
}
