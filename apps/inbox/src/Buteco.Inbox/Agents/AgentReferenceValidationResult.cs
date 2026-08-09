namespace Buteco.Inbox.Agents;

public enum AgentReferenceValidationResult
{
    Found,
    NotFound,

    // Qualquer resposta HTTP diferente de 200/404 (ex. 500) ou exceção de
    // transporte (timeout, conexão recusada, host inalcançável) — os dois
    // são tratados da mesma forma pelo caller (design.md, Decision 4).
    CommunicationFailure,
}
