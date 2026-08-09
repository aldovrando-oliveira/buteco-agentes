using System.Net;

namespace Buteco.Inbox.Agents;

/// <summary>
/// Chama <c>GET /agents/{id}</c> em apps/api para confirmar que um
/// <c>AgentId</c> referenciado por um Channel existe de verdade — não há FK
/// possível entre apps/inbox e apps/api (design.md, Decision 1). Mapeamento
/// de resultado: HTTP 200 → <see cref="AgentReferenceValidationResult.Found"/>
/// (independente do campo <c>isActive</c> no payload — este validador não o
/// inspeciona; canal vinculado a agente inativo é permitido de propósito,
/// ver design.md, Decision 7); HTTP 404 →
/// <see cref="AgentReferenceValidationResult.NotFound"/>; qualquer outro
/// status (ex. 500) ou falha de transporte (timeout, conexão recusada, host
/// inalcançável) → <see cref="AgentReferenceValidationResult.CommunicationFailure"/>.
/// </summary>
public sealed class AgentReferenceValidator(IHttpClientFactory httpClientFactory) : IAgentReferenceValidator
{
    public const string HttpClientName = "AgentReferenceValidator";

    public async Task<AgentReferenceValidationResult> ValidateAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        try
        {
            var response = await client.GetAsync($"/agents/{agentId}", cancellationToken);

            return response.StatusCode switch
            {
                HttpStatusCode.OK => AgentReferenceValidationResult.Found,
                HttpStatusCode.NotFound => AgentReferenceValidationResult.NotFound,
                _ => AgentReferenceValidationResult.CommunicationFailure,
            };
        }
        catch (HttpRequestException)
        {
            return AgentReferenceValidationResult.CommunicationFailure;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout do HttpClient (não cancelamento explícito do caller) —
            // .NET representa isso como TaskCanceledException, não
            // HttpRequestException.
            return AgentReferenceValidationResult.CommunicationFailure;
        }
    }
}
